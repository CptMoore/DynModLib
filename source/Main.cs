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

namespace DynModLib
{
    public static class Main
    {
        public static ILog logger = Logger.GetLogger(ModName);

        private const string ModName = "DynModLib";

        private static string ModsDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static string LogFile => Path.Combine(ModsDirectory, ModName + ".log");

        public static void Init()
        {
            try
            {
                Logger.AddAppender(ModName, new FileLogAppender(LogFile, FileLogAppender.WriteMode.INSTANT));

                var compilerAssembly = Assembly.LoadFrom(Path.Combine(ModsDirectory, "Mono.CSharp.dll"));
                var references = CollectReferences();
                var compiler = new ModCompiler(compilerAssembly, references);

                Directory.GetDirectories(ModsDirectory)
                    .Where(d => File.Exists(Path.Combine(d, Path.Combine("source", "Control.cs"))))
                    .Do(compiler.CompileAndLoad);

                Logger.ClearAppender(ModName);
            }
            catch (Exception e)
            {
                logger.LogError("could not initialize", e);
            }
        }

        private static List<string> CollectReferences()
        {
            var references = GetManagedAssemblyPaths();
            references.Add(Assembly.GetExecutingAssembly().Location);
            return references;
        }

        private static List<string> GetManagedAssemblyPaths()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().GetReferencedAssemblies()
                .Where(assemblyName => assemblyName.Name == "mscorlib")
                .Select(assemblyName => Assembly.ReflectionOnlyLoad(assemblyName.FullName).Location)
                .Select(Path.GetDirectoryName)
                .Single();

            return Directory.GetFiles(assemblyLocation, "*.dll").ToList();
        }

        public static void Reset()
        {
        }
    }

    internal class ModCompiler
    {
        private readonly Assembly compilerAssembly;
        private readonly List<string> references;

        internal ModCompiler(Assembly compilerAssembly, List<string> references)
        {
            this.compilerAssembly = compilerAssembly;
            this.references = references;
        }

        private Mod mod;

        internal void CompileAndLoad(string modDirectory)
        {
            mod = new Mod(modDirectory);
            mod.SetupLogging();

            try
            {
                Main.logger.Log($"{mod.Name}: detected {mod.Directory}");
                mod.Logger.Log("DynModLib: detected {mod.Directory}");

                Compile();
                Load();
                Main.logger.Log($"{mod.Name}: initialized mod");
                mod.Logger.Log($"DynModLib: initialized mod {mod.Name}");
            }
            catch (Exception e)
            {
                Main.logger.Log($"{mod.Name}: could not compile assembly");
                mod.Logger.Log(e.Message, e.InnerException ?? e);
                mod.ShutdownLogging();
            }
        }

        private void Compile()
        {
            var sourceFiles = Directory.GetFiles(mod.SourcePath, "*.cs", SearchOption.AllDirectories);
            CompileAssembly(mod.AssemblyPath, references, sourceFiles.ToList());
        }

        private void Load()
        {
            Assembly assembly;
            try
            {
                assembly = Assembly.LoadFrom(mod.AssemblyPath);
            }
            catch (Exception e)
            {
                throw new Exception($"DynModLib: error loading assembly {mod.AssemblyPath}", e);
            }

            // TODO: remove loading once integrated into ModTek
            var controlClass = mod.Name + ".Control";
            var type = assembly.GetType(controlClass);
            if (type == null)
            {
                throw new Exception($"DynModLib: Can't find class \"{controlClass}\"");
            }
            var method = type.GetMethod("Start", BindingFlags.Public | BindingFlags.Static, null, CallingConventions.Standard, new[] { typeof(string), typeof(string) }, null);
            if (method == null)
            {

                throw new Exception($"DynModLib: Can't find static method \"Start(string modDirectory, string json)\" on class \"{controlClass}\"");
            }
            try
            {
                method.Invoke(method, new object[] { mod.Directory, "{}" });
            }
            catch (Exception e)
            {
                throw new Exception($"DynModLib: error initializing mod {mod.Name}", e);
            }
        }

        private void CompileAssembly(string outPath, List<string> refPaths, List<string> srcPaths)
        {
            if (HasCachedAssembly(outPath, srcPaths))
            {
                Main.logger.Log($"{mod.Name}: found up-to-date assembly {outPath}");
                mod.Logger.Log($"DynModLib: found up-to-date assembly {outPath}");
                return;
            }

            try
            {
                var arguments = new List<string>();
                arguments.Add("/target:library");
                arguments.Add("/nostdlib+");
                arguments.Add("/noconfig");
                arguments.Add("/debug-");
                arguments.Add("/optimize+");
                arguments.Add("/out:" + outPath);
                refPaths.ForEach(r => arguments.Add("/r:" + r));
                srcPaths.ForEach(s => arguments.Add(s));

                var memory = new MemoryStream();
                var writer = new StreamWriter(memory, Encoding.UTF8);
                var type = compilerAssembly.GetType("Mono.CSharp.CompilerCallableEntryPoint");
                var method = type.GetMethod("InvokeCompiler");
                // CompilerCallableEntryPoint.InvokeCompiler(arguments.ToArray(), writer)
                var result = (bool)method.Invoke(method, new object[] { arguments.ToArray(), writer });
                writer.Flush();
                if (result)
                {
                    Main.logger.Log($"{mod.Name}: compiled assembly {outPath}");
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
                throw new Exception("DynModLib: Could not call Mono.CSharp compiler", e);
            }
        }

        private static bool HasCachedAssembly(string outPath, List<string> srcPaths)
        {
            if (!File.Exists(outPath))
            {
                return false;
            }

            var assemblyDateTime = File.GetLastWriteTime(outPath);
            foreach (var sourceFile in srcPaths)
            {
                var sourceDateTime = File.GetLastWriteTime(sourceFile);
                if (sourceDateTime.Subtract(assemblyDateTime).Ticks > 0)
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

        public string AssemblyPath => Path.Combine(Directory, Name + ".dll");
        public string SourcePath => Path.Combine(Directory, "source");
        public string SettingsPath => Path.Combine(Directory, "Settings.json");

        public ILog Logger => HBS.Logging.Logger.GetLogger(Name);
        private FileLogAppender logAppender;

        public void LoadSettings<T>(T settings) where T : ModSettings
        {
            if (!File.Exists(SettingsPath))
            {
                return;
            }

            string json;
            using (var reader = new StreamReader(SettingsPath))
            {
                json = reader.ReadToEnd();
            }
            JSONSerializationUtility.FromJSON(settings, json);

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
            var json = JSONSerializationUtility.ToJSON(settings);
            using (var writer = new StreamWriter(SettingsPath))
            {
                writer.Write(json);
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
            HBS.Logging.Logger.ClearAppender(Name);
            logAppender.Flush();
            logAppender.Close();
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
}
