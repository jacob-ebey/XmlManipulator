using ConsoleNet;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Linq;

namespace UpdateAndroidManifest
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Application app = new Application("Update Android Manifest", new FrameworkCommand[]
                    {
                        new FrameworkCommand("Merge", UpdateManifest)
                        {
                            Description = "Merges the specified config into the manifest file.",
                            Help =
                            "Params:\n" +
                            "--manifest   | The file to update.\n" +
                            "--config     | The file to get new values from.\n\n" +
                            "--forceCache | Force a new cache file to be created if one exists." +
                            "Exit code 0 means success.\n" +
                            "Exit code 1 means missing param.\n" +
                            "Exit code 2 means a file could not be found.\n" +
                            "Exit code 3 is an unknown fatal error.\n" +
                            "Exit code 4 means the manifest file could not be cached."
                        },
                        new FrameworkCommand("Restore", RestoreManifest)
                        {
                            Description = "Restores the specified manifest if there is a cached version.",
                            Help =
                            "Params:\n" +
                            "--manifest | The file to restore.\n\n" +
                            "Exit code 0 means success.\n" +
                            "Exit code 1 means missing param.\n" +
                            "Exit code 2 means a file could not be found.\n" +
                            "Exit code 3 is an unknown fatal error.\n" +
                            "Exit code 4 means the manifest has not been cached and can't be restored."
                        }
                    });

                app.Run(args);
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{e.Message}\n{e.StackTrace}");
                Environment.Exit(3);
            }
        }

        private static void RestoreManifest(Application app)
        {
            try
            {
                string manifest = null;
                if (!app.TryGetParam("manifest", out manifest))
                {
                    Console.Error.WriteLine("--manifest is required.");
                    Environment.Exit(1);
                }

                Console.WriteLine($"Looking for cached version of {Path.GetFileName(manifest)}...");
                var cachedFilePath = Path.GetFullPath(Path.Combine("cache", Path.GetFileName(manifest)));
                if (!File.Exists(cachedFilePath))
                {
                    Console.Error.WriteLine("\tThis manifest has not been cahced.");
                    Environment.Exit(4);
                }
                Console.WriteLine("\tFound cached file");

                Console.WriteLine("Restoring cached file...");
                File.Copy(cachedFilePath, manifest, true);
                Console.WriteLine("\tRestored cached file");

                Console.WriteLine("Removing cached file...");
                File.Delete(cachedFilePath);
                Console.WriteLine("\tCached file removed");

                Console.WriteLine("\nFinished!");
            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{e.Message}\n{e.StackTrace}");
                Environment.Exit(3);
            }
        }

        private static void UpdateManifest(Application app)
        {
            try
            {
                string manifest = null;
                if (!app.TryGetParam("manifest", out manifest))
                {
                    Console.Error.WriteLine("--manifest is required.");
                    Environment.Exit(1);
                }

                string config = null;
                if (!app.TryGetParam("config", out config))
                {
                    Console.Error.WriteLine("--config is required.");
                    Environment.Exit(1);
                }

                string temp;
                bool forceCache = app.TryGetParam("forceCache", out temp);

                if (!File.Exists(manifest))
                {
                    Console.Error.WriteLine("Manifest file can not be found.");
                    Environment.Exit(2);
                }

                if (!File.Exists(config))
                {
                    Console.Error.WriteLine("Config file can not be found.");
                    Environment.Exit(2);
                }
                XDocument manifestDocument, configDocument;

                Console.WriteLine("Reading manifest file...");
                using (XmlTextReader manifestReader = new XmlTextReader(manifest))
                    manifestDocument = XDocument.Load(manifestReader);
                Console.WriteLine("\tFinished reading manifest file");

                Console.WriteLine("Reading config file...");
                using (XmlTextReader configReader = new XmlTextReader(config))
                    configDocument = XDocument.Load(configReader);
                Console.WriteLine("\tFinished reading config file");

                Console.WriteLine("Storing cahced version of manifest...");
                if (!Directory.Exists("cache"))
                    Directory.CreateDirectory("cache");

                var cachedFilePath = Path.GetFullPath(Path.Combine("cache", Path.GetFileName(manifest)));

                try { File.Copy(manifest, cachedFilePath, forceCache); }
                catch { Console.WriteLine("\tA cached version already exists. To force new cache use --forceCache"); }

                if (!File.Exists(cachedFilePath))
                {
                    Console.Error.WriteLine("\tCaching manifest failed.");
                    Environment.Exit(4);
                }
                Console.WriteLine("\tManifest cached");

                Console.WriteLine("Merging config into manifest...");

                var addElements = configDocument.Descendants().Where(e => e.Attribute(XName.Get("add", "http://schemas.omax.com/xml-merge")) != null);

                foreach (var element in addElements.ToArray())
                {
                    var addAttr = element.Attribute(XName.Get("add", "http://schemas.omax.com/xml-merge"));
                    addAttr.Remove();

                    if (addAttr?.Value == "IfNotExist")
                    {
                        var conditionAttr = element.Attribute(XName.Get("condition", "http://schemas.omax.com/xml-merge"));
                        conditionAttr.Remove();

                        string conditionValue = conditionAttr.Value[0] == '!' ? conditionAttr.Value.Substring(1) : conditionAttr.Value;
                        bool negate = conditionAttr.Value[0] == '!';

                        var conditionElement = element.Descendants().Where(e => e.Attribute(XName.Get("newCondition", "http://schemas.omax.com/xml-merge"))?.Value == conditionValue).FirstOrDefault();

                        if (conditionElement == null)
                            throw new Exception($"Could not find condition {conditionValue} in child elements.");

                        conditionElement.Attribute(XName.Get("newCondition", "http://schemas.omax.com/xml-merge")).Remove();

                        bool shouldAdd = manifestDocument.Descendants(element.Name).Descendants(conditionElement.Name).Any(e => XNode.DeepEquals(e, conditionElement));
                        if (negate) shouldAdd = !shouldAdd;

                        if (shouldAdd)
                        {
                            var e = manifestDocument.Descendants(element.Parent.Name).FirstOrDefault();
                            e.Add(element);
                        }
                        else
                        {
                            Console.WriteLine($"\tWarning: Did not merge element {element.Name}");
                        }
                    }
                }
                Console.WriteLine("\tMerged manifest and config");

                Console.WriteLine("Writing newly created merged manifest...");
                manifestDocument.Save(manifest, SaveOptions.OmitDuplicateNamespaces);
                Console.WriteLine("\tFinished writing new manifest");

                Console.WriteLine("\nFinished!");

            }
            catch (Exception e)
            {
                Console.Error.WriteLine($"{e.Message}\n{e.StackTrace}");
                Environment.Exit(3);
            }
        }
    }
}
