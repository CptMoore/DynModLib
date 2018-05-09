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

namespace DynTechMod
{
    public static class Main
    {
        private static ILog logger;

        private const string ModName = "DynTechMod";

        private static string ModsDirectory => Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        private static string ModDirectory => Path.Combine(ModsDirectory, ModName);

        private static List<string> references = new List<string>();

        private static Assembly compilerAssembly;

        public static void Init()
        {
            var self = new Mod(ModDirectory);
            var settings = new ModSettings();
            self.LoadSettings(settings);
            logger = self.Logger;

            var harmony = HarmonyInstance.Create(ModName);
            harmony.PatchAll(Assembly.GetExecutingAssembly());

            references.AddRange(GetManagedAssemblyPaths());
            references.Add(Assembly.GetExecutingAssembly().Location);

            compilerAssembly = Assembly.LoadFrom(Path.Combine(ModDirectory, "Mono.CSharp.dll"));

            Directory.GetDirectories(ModsDirectory)
                .Select(m => Path.Combine(m, "source\\Control.cs"))
                .Where(File.Exists)
                .Do(logger.LogDebug)
                .Do(LoadDynamicMod);
        }

        public static void Reset()
        {
        }

        private static string[] GetManagedAssemblyPaths()
        {
            var assemblyLocation = Assembly.GetExecutingAssembly().GetReferencedAssemblies()
                .Where(assemblyName => assemblyName.Name == "mscorlib")
                .Select(assemblyName => Assembly.ReflectionOnlyLoad(assemblyName.FullName).Location)
                .Select(Path.GetDirectoryName)
                .Single();

            return Directory.GetFiles(assemblyLocation, "*.dll");
        }

        private static void LoadDynamicMod(string controlFilePath)
        {
            var sourcesPath = Path.GetDirectoryName(controlFilePath);
            var mod = new Mod(Path.GetDirectoryName(sourcesPath));

            var sourceFiles = Directory.GetFiles(sourcesPath, "*.cs", SearchOption.AllDirectories);
            var assembly = CompileAssembly(mod.AssemblyPath, references, sourceFiles.ToList());
            if (assembly == null)
            {
                logger.LogError("Error compiling mod " + mod.Name);
                return;
            }

            var controlClass = mod.Name + ".Control";
            var type = assembly.GetType(controlClass);
            if (type == null)
            {
                logger.LogError("Can't find class \"" + controlClass + "\"");
                return;
            }
            var method = type.GetMethod("OnInit", BindingFlags.Public | BindingFlags.Static, null, CallingConventions.Standard, new[]{typeof(Mod)}, null);
            if (method == null)
            {
                logger.LogError("Can't find static method \"OnInit(Mod mod)\" on class \"" + controlClass + "\"");
                return;
            }
            //logger.LogDebug(method.ToString());
            try
            {
                method.Invoke(method, new object[] {mod});
            }
            catch (Exception e)
            {
                logger.LogError("error initializing mod " + mod.Name, e);
            }

            logger.Log("initialized mod " + mod.Name);
        }

        private static Assembly CompileAssembly(string outPath, List<string> refPaths, List<string> srcPaths)
        {
            try
            {
                {
                    var cachedAssembly = GetCachedAssembly(outPath, srcPaths);
                    if (cachedAssembly != null)
                    {
                        logger.Log("found cached assembly " + outPath);
                        return cachedAssembly;
                    }
                }

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
                    logger.Log("compiled assembly " + outPath);
                    return Assembly.LoadFrom(outPath);
                }
                else
                {
                    memory.Position = 0;
                    var reader = new StreamReader(memory);
                    logger.LogError("csc could not compile assembly " + outPath);
                    logger.LogError("csc output: " + reader.ReadToEnd());
                    return null;
                }
            }
            catch (Exception e)
            {
                logger.LogError("could not start csc process", e);
            }

            return null;
        }

        private static Assembly GetCachedAssembly(string outPath, List<string> srcPaths)
        {
            if (!File.Exists(outPath))
            {
                return null;
            }

            var assemblyDateTime = File.GetLastWriteTime(outPath);
            foreach (var sourceFile in srcPaths)
            {
                var sourceDateTime = File.GetLastWriteTime(sourceFile);
                if (sourceDateTime.Subtract(assemblyDateTime).Ticks > 0)
                {
                    return null;
                }
            }

            return Assembly.LoadFrom(outPath);
        }
    }

    public class Mod
    {
        internal Mod(string directory)
        {
            Directory = directory;
            Name = Path.GetFileName(directory);

            const string logFile = "log.txt";
            if (!string.IsNullOrEmpty(logFile))
            {
                var logFilePath = Path.Combine(Directory, logFile);
                var appender = new FileLogAppender(logFilePath, FileLogAppender.WriteMode.INSTANT);

                HBS.Logging.Logger.AddAppender(Name, appender);
                HBS.Logging.Logger.SetLoggerLevel(Name, LogLevel.Debug);
            }
        }

        public string Name { get; }
        public string Directory { get; }

        public string AssemblyPath => Path.Combine(Directory, "..\\" + Name + ".dll");
        private string SettingsPath => Path.Combine(Directory, "Settings.json");
        //public string ManifestPath => Path.Combine(ModDirectory, "VersionManifest.csv");

        public ILog Logger => HBS.Logging.Logger.GetLogger(Name);

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
            if (level != null)
            {
                HBS.Logging.Logger.SetLoggerLevel(Name, level);
            }
        }

        public void SaveSettings<T>(T settings) where T : ModSettings
        {
            var json = JSONSerializationUtility.ToJSON(settings);
            using (var writer = new StreamWriter(SettingsPath))
            {
                writer.Write(json);
            }
        }
    }

    public class ModSettings
    {
        // after loading settings the log level will revert to Warning from the initial Debug
        public string logLevel = "Warning";
    }
}
