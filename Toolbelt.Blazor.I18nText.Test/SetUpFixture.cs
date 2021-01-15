﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Xml.XPath;
using NUnit.Framework;
using Toolbelt.Blazor.I18nText.Test.Internals;
using static Toolbelt.Blazor.I18nText.Test.Internals.Shell;
using static Toolbelt.Blazor.I18nText.Test.Internals.ProcessZ;
using System.Threading.Tasks;

namespace Toolbelt.Blazor.I18nText.Test
{
    [SetUpFixture]
    public class SetUpFixture
    {
        [OneTimeSetUp]
        public async Task SetupAsync()
        {
            var latestI18nTextVersion = GetLatestI18nTextPackageVersion();
            var versionOfLibProjects = await UpdateLibraryProjectsAsync("Toolbelt.Blazor.I18nText", latestI18nTextVersion);
            await UpdatePackageRefAsync(latestI18nTextVersion, versionOfLibProjects);
        }

        private static string GetLatestI18nTextPackageVersion()
        {
            var testDir = WorkSpace.GetTestDir();
            var nugetConfigPath = Path.Combine(testDir, "nuget.config");
            var nugetConfigXdoc = XDocument.Load(nugetConfigPath);
            var distDir = nugetConfigXdoc.XPathSelectElement("//packageSources/add[@key='I18nText']").Attribute("value").Value;
            var packageFiles = Directory.GetFiles(Path.Combine(testDir, distDir), "*.nupkg");
            var latestI18nTextVersion = packageFiles
                .Select(path => Regex.Match(path, @"Toolbelt\.Blazor\.I18nText\.(?<ver>[\d\.]+).nupkg$"))
                .Where(m => m.Success)
                .Select(m => m.Groups["ver"].Value)
                .OrderBy(v => Version.Parse(v))
                .Last();
            return latestI18nTextVersion;
        }

        private static Task<IReadOnlyDictionary<string, string>> UpdateLibraryProjectsAsync(string packageName, string latestPackageVersion)
        {
            var projectNames = new[] {
                Path.Combine("Lib4PackRef","Lib4PackRef"),
                Path.Combine("Lib4ProjRef", "Lib4ProjRef")};
            return UpdateReferencedPackageVersionAsync(packageName, latestPackageVersion, projectNames, buildIfChanges: true);
        }

        private static async Task UpdatePackageRefAsync(string latestI18nTextVersion, IReadOnlyDictionary<string, string> versionOfLibProjects)
        {
            var projectNames = new[] {
                Path.Combine("Components", "SampleSite.Components"),
                Path.Combine("Client", "SampleSite.Client"),
                Path.Combine("Server", "SampleSite.Server")};
            await UpdateReferencedPackageVersionAsync("Toolbelt.Blazor.I18nText", latestI18nTextVersion, projectNames, buildIfChanges: false);

            await UpdateReferencedPackageVersionAsync("Lib4PackRef", versionOfLibProjects["Lib4PackRef"], projectNames.Where(n => n.StartsWith("Components")), buildIfChanges: false);
        }

        private static async Task<IReadOnlyDictionary<string, string>> UpdateReferencedPackageVersionAsync(string packageName, string latestPackageVersion, IEnumerable<string> projectNames, bool buildIfChanges)
        {
            var versionOfProjects = new Dictionary<string, string>();

            var testDir = WorkSpace.GetTestDir();
            foreach (var projectSubPath in projectNames)
            {
                var projectPath = Path.Combine(testDir, projectSubPath + ".csproj");
                var projectDir = Path.GetDirectoryName(projectPath);
                var projectName = Path.GetFileNameWithoutExtension(projectPath);
                var projectXDoc = XDocument.Load(projectPath);

                var projectVersionNode = projectXDoc.XPathSelectElement("//Version");
                var projectCurrentVersion = projectVersionNode?.Value ?? "1.0.0.0";
                versionOfProjects.Add(projectName, projectCurrentVersion);

                var packageRef = projectXDoc.XPathSelectElement($"//PackageReference[@Include='{packageName}']");
                var referencedVersion = packageRef.Attribute("Version").Value;

                if (referencedVersion != latestPackageVersion)
                {
                    packageRef.SetAttributeValue("Version", latestPackageVersion);

                    var baseVer = Version.Parse(projectCurrentVersion);
                    var projectNextVersion = $"{baseVer.ToString(3)}.{baseVer.Revision + 1}";
                    if (projectVersionNode != null) projectVersionNode.Value = projectNextVersion;
                    versionOfProjects[projectName] = projectNextVersion;

                    projectXDoc.Save(projectPath);
                }

                await using var restoreProcess = await Start("dotnet", "restore", projectDir).WaitForExitAsync();
                restoreProcess.ExitCode.Is(0, message: restoreProcess.Output);

                if (referencedVersion != latestPackageVersion && buildIfChanges)
                {
                    await using var buildProcess = await Start("dotnet", "build", projectDir).WaitForExitAsync();
                    buildProcess.ExitCode.Is(0, message: buildProcess.Output);
                }
            }

            return versionOfProjects;
        }
    }
}
