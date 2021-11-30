using System;
using System.IO;
using Harmony;
using System.Reflection;
using HBS.Util;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using BattleTech;
using HBS.Logging;
using Mono.CSharp;

namespace DynModLib
{
    public static class Main
    {
        internal static Mod lib;
        internal static Assembly compilerAssembly;

        public static void Start(string modDirectory, string json)
        {
            var sw = new StopWatch();
            sw.Start();
            lib = new Mod(modDirectory);
            lib.SetupLogging();
            try
            {
                compilerAssembly = Assembly.LoadFrom(Path.Combine(lib.Directory, "Mono.CSharp.dll"));

                HarmonyInstance.Create("DynModLib.CSharp").PatchAll();

                var references = CollectReferences();
                lib.Logger.LogDebug($"Found references:");
                foreach (var reference in references)
                {
                    lib.Logger.LogDebug($"\t{reference}");
                }
                var compiler = new ModCompiler(compilerAssembly, references);

                foreach (var d in Directory.GetDirectories(lib.ModsPath)
                    .Where(d => File.Exists(Path.Combine(d, "mod.json"))))
                {
                    compiler.CheckAndCompile(d);
                }
            }
            catch (Exception e)
            {
                lib.Logger.LogError("could not initialize", e);
            }
            finally
            {
                sw.Stop();
                lib.Logger.Log($"Checked and compiled mods in {sw.Elapsed.TotalMilliseconds}ms");
                lib.ShutdownLogging();
            }
        }

        private static List<string> CollectReferences()
        {
            var references = GetManagedAssemblyPaths();
            references.Add(Assembly.GetExecutingAssembly().Location);
            references.RemoveAll(x=> x.Contains("System.Runtime."));
            references.RemoveAll(x=> x.Contains("System.ValueTuple."));
            return references;
        }

        private static List<string> GetManagedAssemblyPaths()
        {
            var location = GetAssemblyByName("Assembly-CSharp").Location;
            var directory = Path.GetDirectoryName(location);
            return Directory.GetFiles(directory, "*.dll").ToList();
        }

        private static Assembly GetAssemblyByName(string name)
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .Single(assembly => assembly.GetName().Name == name);
        }

        public static void Reset()
        {
        }
    }

    [HarmonyPatch(typeof(MetadataImporter), nameof(MetadataImporter.GetAssemblyDefinition))]
    internal static class MetadataImporter_GetAssemblyDefinition_Patch
    {
        private static Assembly lastLoadingAssembly;
        public static void Prefix(Assembly assembly)
        {
            lastLoadingAssembly = assembly;
        }

        public static void Postfix()
        {
            lastLoadingAssembly = null;
        }

        internal static void LogIfError()
        {
            if (lastLoadingAssembly != null)
            {
                Main.lib.Logger.LogError($"Couldn't load {lastLoadingAssembly} from {lastLoadingAssembly.Location}!");
            }
        }
    }

    [HarmonyPatch]
    internal static class AssemblyDefinition_CheckReferencesPublicToken_Patch
    {
        public static MethodInfo TargetMethod()
        {
            return Main.compilerAssembly.GetType("Mono.CSharp.AssemblyDefinition")
                .GetMethod("CheckReferencesPublicToken", BindingFlags.Instance | BindingFlags.NonPublic);
        }

        public static bool Prefix()
        {
            return false;
        }
    }

    internal class ModCompiler
    {
        private readonly List<string> references;
        private readonly DateTime lastestAssemblyWriteTime;

        internal ModCompiler(Assembly compilerAssembly, List<string> references)
        {
            this.references = references;

            var latestWriteTime = DateTime.MinValue;
            foreach (var reference in references)
            {
                var writeTime = File.GetLastWriteTime(reference);
                if (writeTime.CompareTo(latestWriteTime) > 0)
                {
                    latestWriteTime = writeTime;
                }
            }
            lastestAssemblyWriteTime = latestWriteTime;
        }

        private Mod mod;

        internal void CheckAndCompile(string modDirectory)
        {
            try
            {
                var sw = new StopWatch();
                sw.Start();
                mod = new Mod(modDirectory);
                if (!mod.DependsOnDynModLib || !Directory.Exists(mod.SourcePath))
                {
                    return;
                }

                Main.lib.Logger.Log($"detected {mod}");
                mod.SetupLogging();

                if (mod.AssemblyPath == null)
                {
                    throw new Exception("Assembly DLL is not set");
                }

                Compile();

                sw.Stop();
                Main.lib.Logger.Log($"{mod.Name}: prepared assembly in {sw.Elapsed.TotalMilliseconds}ms");
            }
            catch (Exception e)
            {
                Main.lib.Logger.Log($"{mod.Name}: error preparing assembly");
                mod.Logger.Log(e.Message, e.InnerException ?? e);
                mod.ShutdownLogging();
            }
            finally
            {
            }
        }

        private void Compile()
        {
            var sourceFiles = Directory.GetFiles(mod.SourcePath, "*.cs", SearchOption.AllDirectories);
            CompileAssembly(mod.AssemblyPath, references, sourceFiles.ToList());
        }

        private void CompileAssembly(string outPath, List<string> refPaths, List<string> srcPaths)
        {
            if (HasCachedAssembly(outPath, srcPaths))
            {
                mod.Logger.Log($"DynModLib: found up-to-date assembly {outPath}");
                return;
            }

            try
            {
                var arguments = new List<string>
                {
                    "/target:library",
                    "/nostdlib+",
                    "/noconfig",
                    "/debug-",
                    "/optimize+",
                    // "/platform:anycpu",
                    "/langversion:latest",
                    // "/unsafe+",
                    // "/checked-",
                    "/out:" + outPath
                };
                refPaths.ForEach(r => arguments.Add("/r:" + r));
                srcPaths.ForEach(s => arguments.Add(s));

                var memory = new MemoryStream();
                var writer = new StreamWriter(memory, Encoding.UTF8);
                var type = Main.compilerAssembly.GetType("Mono.CSharp.CompilerCallableEntryPoint");
                var method = type.GetMethod("InvokeCompiler");
                // CompilerCallableEntryPoint.InvokeCompiler(arguments.ToArray(), writer)
                var result = (bool)method.Invoke(method, new object[] { arguments.ToArray(), writer });
                writer.Flush();
                if (result)
                {
                    mod.Logger.Log($"DynModLib: compiled assembly {outPath}");
                }
                else
                {
                    memory.Position = 0;
                    var reader = new StreamReader(memory);
                    var output = reader.ReadToEnd();
                    throw new Exception($"DynModLib: Mono.CSharp could not compile assembly {outPath}, output {output}");
                }
            }
            catch (Exception e)
            {
                MetadataImporter_GetAssemblyDefinition_Patch.LogIfError();
                throw new Exception("DynModLib: Could not call Mono.CSharp compiler", e);
            }
        }

        private bool HasCachedAssembly(string outPath, List<string> srcPaths)
        {
            if (!File.Exists(outPath))
            {
                return false;
            }

            var assemblyDateTime = File.GetLastWriteTime(outPath);

            if (lastestAssemblyWriteTime.CompareTo(assemblyDateTime) > 0)
            {
                return false;
            }

            foreach (var sourceFile in srcPaths)
            {
                var sourceDateTime = File.GetLastWriteTime(sourceFile);
                if (sourceDateTime.CompareTo(assemblyDateTime) > 0)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public class Mod
    {
        public Mod(string directory)
        {
            Directory = directory;
            Name = Path.GetFileName(directory);
        }

        public string Name { get; }
        public string Directory { get; }

        public string SourcePath => Path.Combine(Directory, "source");
        public string SettingsPath => Path.Combine(Directory, "Settings.json");
        public string ModsPath => Path.GetDirectoryName(Directory);
        public string InfoPath => Path.Combine(Directory, "mod.json");

        public ILog Logger => HBS.Logging.Logger.GetLogger(Name);
        private FileLogAppender logAppender;

        public void LoadSettings<T>(T settings) where T : ModSettings
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            using (var reader = new StreamReader(SettingsPath))
            {
                var json = reader.ReadToEnd();
                JSONSerializationUtility.FromJSON(settings, json);
            }

            var logLevelString = settings.logLevel;
            DebugBridge.StringToLogLevel(logLevelString, out var level);
            if (level == null)
            {
                level = LogLevel.Debug;
            }
            HBS.Logging.Logger.SetLoggerLevel(Name, level);
        }

        public void SaveSettings<T>(T settings) where T : ModSettings
        {
            using (var writer = new StreamWriter(SettingsPath))
            {
                var json = JSONSerializationUtility.ToJSON(settings);
                writer.Write(json);
            }
        }

        internal bool DependsOnDynModLib => ModTekInfo.DependsOn.Contains(Main.lib.Name);
        internal string AssemblyPath => string.IsNullOrEmpty(ModTekInfo.DLL) ? null : Path.Combine(Directory, ModTekInfo.DLL);

        private ModTekInfo _modTekInfo;
        internal ModTekInfo ModTekInfo
        {
            get
            {
                if (_modTekInfo == null)
                {
                    using (var reader = new StreamReader(InfoPath))
                    {
                        var info = new ModTekInfo();
                        var json = reader.ReadToEnd();
                        JSONSerializationUtility.FromJSON(info, json);
                        _modTekInfo = info;
                    }
                }

                return _modTekInfo;
            }
        }

        internal void SetupLogging()
        {
            var logFilePath = Path.Combine(Directory, "log.txt");
            try
            {
                ShutdownLogging();
                AddLogFileForLogger(Name, logFilePath);
            }
            catch (Exception e)
            {
                Logger.Log("DynModLib: can't create log file", e);
            }
        }

        internal void ShutdownLogging()
        {
            if (logAppender == null)
            {
                return;
            }

            try
            {
                HBS.Logging.Logger.ClearAppender(Name);
                logAppender.Flush();
                logAppender.Close();
            }
            catch
            {
            }

            logAppender = null;
        }

        private void AddLogFileForLogger(string name, string logFilePath)
        {
            logAppender = new FileLogAppender(logFilePath, FileLogAppender.WriteMode.INSTANT);

            HBS.Logging.Logger.AddAppender(name, logAppender);
        }

        public override string ToString()
        {
            return $"{Name} ({Directory})";
        }
    }

    public class ModSettings
    {
        public string logLevel = "Log";
    }

    internal class ModTekInfo
    {
        public string[] DependsOn = { };
        public string DLL = null;
    }

    public class Adapter<T>
    {
        public readonly T instance;
        public readonly Traverse traverse;

        protected Adapter(T instance)
        {
            this.instance = instance;
            traverse = Traverse.Create(instance);
        }
    }

    internal class StopWatch
    {
        public TimeSpan Elapsed { get; private set; }
        public DateTime StartDateTime { get; private set; }
        public DateTime StopDateTime { get; private set; }
        public void Start()
        {
            StartDateTime = DateTime.Now;
        }

        public void Stop()
        {
            StopDateTime = DateTime.Now;
            Elapsed = StopDateTime.Subtract(StartDateTime);
        }
    }
}
