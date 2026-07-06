using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;

namespace ModPlusVSTools.Services
{
    /// <summary>
    /// Проект-кандидат на сборку (проект плагина ModPlus).
    /// </summary>
    internal sealed class PluginProjectCandidate
    {
        public PluginProjectCandidate(Project project, List<string> configurations)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Project = project;
            Configurations = configurations;
            Name = project.Name;
            UniqueName = project.UniqueName;
            FullName = project.FullName;

            try
            {
                var configurationManager = project.ConfigurationManager;
                if (configurationManager != null)
                {
                    var platforms = ((Array)configurationManager.PlatformNames)
                        .Cast<object>()
                        .Select(o => o.ToString())
                        .ToList();

                    // "Any CPU" (VS) → "AnyCPU" (MSBuild)
                    MsBuildPlatform = platforms.FirstOrDefault()?.Replace(" ", string.Empty);
                }
            }
            catch
            {
                MsBuildPlatform = null;
            }
        }

        /// <summary>Проект DTE.</summary>
        public Project Project { get; }

        /// <summary>Имя проекта.</summary>
        public string Name { get; }

        /// <summary>Уникальное имя проекта в решении.</summary>
        public string UniqueName { get; }

        /// <summary>Полный путь к файлу проекта.</summary>
        public string FullName { get; }

        /// <summary>Платформа для MSBuild (например, AnyCPU). Может быть null.</summary>
        public string MsBuildPlatform { get; }

        /// <summary>Версионные конфигурации, которые нужно собирать.</summary>
        public IReadOnlyList<string> Configurations { get; }

        /// <summary>Строка для отображения в диалоге выбора.</summary>
        public string DisplayName
        {
            get
            {
                var preview = string.Join(", ", Configurations.Take(5));
                if (Configurations.Count > 5)
                    preview += ", …";
                return string.Format("{0}   ({1} конф.: {2})", Name, Configurations.Count, preview);
            }
        }
    }

    /// <summary>
    /// Результат сборки проекта по списку конфигураций.
    /// </summary>
    internal sealed class BuildRunResult
    {
        /// <summary>Успешно собранные конфигурации.</summary>
        public List<string> Succeeded { get; } = new List<string>();

        /// <summary>Конфигурации, сборка которых завершилась с ошибкой.</summary>
        public List<string> Failed { get; } = new List<string>();
    }

    /// <summary>
    /// Сервис сборки и архивирования проектов плагинов ModPlus.
    /// </summary>
    internal static class BuildService
    {
        /// <summary>
        /// Шаблон "версионной" конфигурации: одна буква + число, опционально с дробной частью.
        /// Примеры: A2013, R2019, N25.0, R5.1, R8.8.
        /// Конфигурации Debug, Release, DebugPekshev, Debug2025 под шаблон не попадают.
        /// </summary>
        private static readonly Regex VersionConfigPattern =
            new Regex(@"^[A-Za-z]\d+(\.\d+)?$", RegexOptions.Compiled);

        /// <summary>
        /// Найти в решении все проекты плагинов (проекты, у которых "большинство"
        /// конфигураций соответствует версионному шаблону).
        /// </summary>
        public static List<PluginProjectCandidate> FindPluginProjects(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<PluginProjectCandidate>();
            foreach (var project in GetAllProjects(dte.Solution))
            {
                var buildConfigs = GetBuildableConfigurations(project);
                if (buildConfigs.Count > 0)
                    result.Add(new PluginProjectCandidate(project, buildConfigs));
            }

            return result;
        }

        /// <summary>
        /// Найти MSBuild.exe текущей установки Visual Studio.
        /// </summary>
        public static string FindMsBuildPath(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                // dte.FullName: {корень установки VS}\Common7\IDE\devenv.exe
                var ideDir = Path.GetDirectoryName(dte.FullName);
                if (ideDir == null)
                    return null;

                var installRoot = Path.GetFullPath(Path.Combine(ideDir, "..", ".."));

                var msBuild = Path.Combine(installRoot, "MSBuild", "Current", "Bin", "MSBuild.exe");
                if (File.Exists(msBuild))
                    return msBuild;

                msBuild = Path.Combine(installRoot, "MSBuild", "Current", "Bin", "amd64", "MSBuild.exe");
                if (File.Exists(msBuild))
                    return msBuild;
            }
            catch
            {
                // вернём null
            }

            return null;
        }

        /// <summary>
        /// Последовательная сборка проекта по каждой из указанных конфигураций.
        /// Каждая конфигурация собирается внешним процессом MSBuild с ключом -restore,
        /// чтобы NuGet restore выполнялся заново для TargetFramework этой конфигурации
        /// (obj\project.assets.json один на проект, а TFM может отличаться по конфигурациям).
        /// Весь вывод MSBuild передаётся в log.
        /// Метод рассчитан на вызов из фонового потока — DTE не используется.
        /// </summary>
        public static BuildRunResult BuildProject(
            string msBuildPath,
            PluginProjectCandidate candidate,
            Action<string> log)
        {
            var result = new BuildRunResult();
            var total = candidate.Configurations.Count;

            for (var i = 0; i < total; i++)
            {
                var config = candidate.Configurations[i];
                log(string.Format("------ Конфигурация {0} ({1} из {2}) ------", config, i + 1, total));

                var arguments = new StringBuilder();
                arguments.Append('"').Append(candidate.FullName).Append('"');
                arguments.Append(" -restore");
                arguments.Append(" -p:Configuration=\"").Append(config).Append('"');
                if (!string.IsNullOrEmpty(candidate.MsBuildPlatform))
                    arguments.Append(" -p:Platform=\"").Append(candidate.MsBuildPlatform).Append('"');
                arguments.Append(" -v:m -nologo");

                try
                {
                    var startInfo = new ProcessStartInfo
                    {
                        FileName = msBuildPath,
                        Arguments = arguments.ToString(),
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,

                        // Современный MSBuild при перенаправлении вывода пишет
                        // локализованный текст в UTF-8; без явного указания кодировки
                        // Process декодирует поток в системной ANSI/OEM-кодировке,
                        // и русский текст превращается в "крякозябры"
                        StandardOutputEncoding = Encoding.UTF8,
                        StandardErrorEncoding = Encoding.UTF8,
                    };


                    using (var process = new System.Diagnostics.Process { StartInfo = startInfo })
                    {
                        process.OutputDataReceived += (s, e) =>
                        {
                            if (e.Data != null)
                                log(e.Data);
                        };
                        process.ErrorDataReceived += (s, e) =>
                        {
                            if (e.Data != null)
                                log(e.Data);
                        };

                        process.Start();
                        process.BeginOutputReadLine();
                        process.BeginErrorReadLine();
                        process.WaitForExit();

                        if (process.ExitCode == 0)
                        {
                            result.Succeeded.Add(config);
                            log(string.Format(">>> {0} — OK", config));
                        }
                        else
                        {
                            result.Failed.Add(config);
                            log(string.Format(">>> {0} — ОШИБКА (код выхода {1})", config, process.ExitCode));
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Failed.Add(config);
                    log(string.Format(">>> {0} — ОШИБКА: {1}", config, ex.Message));
                }

                log(string.Empty);
            }

            return result;
        }

        /// <summary>
        /// Создать zip-архив с dll, полученными при сборке.
        /// Все dll кладутся в корень архива (имена файлов различаются по конфигурациям).
        /// </summary>
        /// <returns>Путь к созданному архиву.</returns>
        public static string CreateArchive(
            PluginProjectCandidate candidate,
            IEnumerable<string> configurations,
            string targetFolder,
            out List<string> missingConfigurations)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            missingConfigurations = new List<string>();
            var zipPath = Path.Combine(targetFolder, candidate.Name + ".zip");

            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var addedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var config in configurations)
                {
                    var dllPath = FindOutputDll(candidate, config);
                    if (dllPath == null)
                    {
                        missingConfigurations.Add(config);
                        continue;
                    }

                    // Все dll — в корень архива. Имена файлов различаются по конфигурациям;
                    // на случай неожиданного совпадения — страховка с префиксом конфигурации
                    var entryName = Path.GetFileName(dllPath);
                    if (!addedNames.Add(entryName))
                        entryName = config + "_" + entryName;

                    zip.CreateEntryFromFile(dllPath, entryName, CompressionLevel.Optimal);
                }
            }

            return zipPath;
        }

        /// <summary>
        /// Найти выходную dll проекта для указанной конфигурации.
        /// Сначала — через группу выходных файлов "Built" (даёт точное имя dll,
        /// даже если AssemblyName различается по конфигурациям),
        /// затем — через OutputPath, в конце — резервный поиск в bin\{конфигурация}.
        /// </summary>
        public static string FindOutputDll(PluginProjectCandidate candidate, string configuration)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var projectDir = Path.GetDirectoryName(candidate.FullName);
            if (projectDir == null)
                return null;

            Configuration cfg = null;
            try
            {
                var configurationManager = candidate.Project.ConfigurationManager;
                if (configurationManager != null)
                {
                    foreach (Configuration c in configurationManager.ConfigurationRow(configuration))
                    {
                        cfg = c;
                        break;
                    }
                }
            }
            catch
            {
                // игнорируем — перейдём к резервным вариантам
            }

            // 1. Группа выходных файлов "Built" — основной результат сборки конфигурации
            if (cfg != null)
            {
                try
                {
                    var builtGroup = cfg.OutputGroups.Item("Built");
                    var urls = ((Array)builtGroup.FileURLs).Cast<object>().Select(o => o.ToString());
                    foreach (var url in urls)
                    {
                        var localPath = new Uri(url).LocalPath;
                        if (localPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) &&
                            File.Exists(localPath))
                        {
                            return localPath;
                        }
                    }
                }
                catch
                {
                    // игнорируем — перейдём к поиску по OutputPath
                }
            }

            var assemblyName =
                TryGetPropertyValue(SafeGetProjectProperties(candidate.Project), "AssemblyName")
                ?? Path.GetFileNameWithoutExtension(candidate.FullName);
            var dllName = assemblyName + ".dll";

            // 2. Через OutputPath конфигурации: сначала по имени сборки,
            //    затем — просто новейшая dll в папке вывода
            try
            {
                var outputPath = cfg == null ? null : TryGetPropertyValue(cfg.Properties, "OutputPath");
                if (!string.IsNullOrEmpty(outputPath))
                {
                    var outDir = Path.GetFullPath(Path.Combine(projectDir, outputPath));
                    var found = FindNewestFile(outDir, dllName) ?? FindNewestFile(outDir, "*.dll");
                    if (found != null)
                        return found;
                }
            }
            catch
            {
                // игнорируем и переходим к резервному поиску
            }

            // 3. Резервный вариант: bin\{конфигурация}
            var binDir = Path.Combine(projectDir, "bin", configuration);
            return FindNewestFile(binDir, dllName) ?? FindNewestFile(binDir, "*.dll");
        }

        private static string FindNewestFile(string directory, string fileName)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
                return null;

            return Directory
                .GetFiles(directory, fileName, SearchOption.AllDirectories)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();
        }

        /// <summary>
        /// Получить версионные конфигурации проекта. Проект считается проектом плагина,
        /// если конфигурации по версионному шаблону составляют большинство среди конфигураций,
        /// не относящихся к Debug*/Release*.
        /// </summary>
        private static List<string> GetBuildableConfigurations(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // 1. Через ConfigurationManager (может быть null для некоторых
            //    SDK-проектов или проектов, ещё не загруженных полностью)
            var allConfigs = new List<string>();
            try
            {
                var configurationManager = project.ConfigurationManager;
                if (configurationManager != null)
                {
                    allConfigs = ((Array)configurationManager.ConfigurationRowNames)
                        .Cast<object>()
                        .Select(o => o.ToString())
                        .ToList();
                }
            }
            catch
            {
                // перейдём к чтению из файла проекта
            }

            // 2. Резервный вариант: свойство <Configurations> из файла проекта
            //    или из Directory.Build.props вверх по папкам
            if (allConfigs.Count == 0)
                allConfigs = ReadConfigurationsFromProjectFiles(project.FullName);

            return FilterVersionConfigurations(allConfigs);
        }

        /// <summary>
        /// Отобрать версионные конфигурации по правилу "большинства".
        /// </summary>
        private static List<string> FilterVersionConfigurations(List<string> allConfigs)
        {
            var versionConfigs = allConfigs
                .Where(c => VersionConfigPattern.IsMatch(c))
                .ToList();

            if (versionConfigs.Count == 0)
                return new List<string>();

            // "Определение по большинству": сравниваем количество версионных конфигураций
            // с количеством прочих (не Debug*/Release*) конфигураций
            var otherCount = allConfigs.Count(c =>
                !VersionConfigPattern.IsMatch(c) &&
                !c.StartsWith("Debug", StringComparison.OrdinalIgnoreCase) &&
                !c.StartsWith("Release", StringComparison.OrdinalIgnoreCase));

            return versionConfigs.Count >= otherCount
                ? versionConfigs
                : new List<string>();
        }

        /// <summary>
        /// Прочитать свойство &lt;Configurations&gt; из файла проекта, а если его там нет —
        /// из ближайшего Directory.Build.props вверх по дереву папок.
        /// </summary>
        private static List<string> ReadConfigurationsFromProjectFiles(string projectPath)
        {
            if (string.IsNullOrEmpty(projectPath) || !File.Exists(projectPath))
                return new List<string>();

            var configs = ReadConfigurationsProperty(projectPath);
            if (configs != null)
                return configs;

            var dir = Path.GetDirectoryName(projectPath);
            while (!string.IsNullOrEmpty(dir))
            {
                var propsFile = Path.Combine(dir, "Directory.Build.props");
                if (File.Exists(propsFile))
                {
                    configs = ReadConfigurationsProperty(propsFile);
                    if (configs != null)
                        return configs;
                }

                dir = Path.GetDirectoryName(dir);
            }

            return new List<string>();
        }

        /// <summary>
        /// Прочитать значение свойства &lt;Configurations&gt; из MSBuild-файла.
        /// Возвращает null, если свойство не найдено.
        /// </summary>
        private static List<string> ReadConfigurationsProperty(string filePath)
        {
            try
            {
                var document = XDocument.Load(filePath);
                var element = document
                    .Descendants()
                    .FirstOrDefault(e => e.Name.LocalName == "Configurations");

                if (element == null)
                    return null;

                return element.Value
                    .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(s => s.Trim())
                    .Where(s => s.Length > 0 && !s.Contains("$("))
                    .ToList();
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Рекурсивно получить все проекты решения (с обходом папок решения).
        /// </summary>
        private static List<Project> GetAllProjects(Solution solution)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var result = new List<Project>();
            if (solution == null)
                return result;

            foreach (Project project in solution.Projects)
                CollectProjects(project, result);

            return result;
        }

        private static void CollectProjects(Project project, List<Project> result)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (project == null)
                return;

            try
            {
                if (project.Kind == ProjectKinds.vsProjectKindSolutionFolder)
                {
                    if (project.ProjectItems == null)
                        return;

                    foreach (ProjectItem item in project.ProjectItems)
                        CollectProjects(item.SubProject, result);
                }
                else if (!string.IsNullOrEmpty(project.FullName) && File.Exists(project.FullName))
                {
                    result.Add(project);
                }
            }
            catch
            {
                // некоторые узлы решения (Misc Files и т.п.) могут бросать исключения — пропускаем
            }
        }

        private static Properties SafeGetProjectProperties(Project project)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                return project.Properties;
            }
            catch
            {
                return null;
            }
        }

        private static string TryGetPropertyValue(Properties properties, string name)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (properties == null)
                return null;

            try
            {
                var value = properties.Item(name)?.Value;
                return value?.ToString();
            }
            catch
            {
                return null;
            }
        }
    }
}
