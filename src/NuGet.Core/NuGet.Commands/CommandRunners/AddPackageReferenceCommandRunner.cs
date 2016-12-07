// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Commands
{
    public class AddPackageReferenceCommandRunner
    {
        public void ExecuteCommand(PackageReferenceArgs packageReferenceArgs)
        {
            packageReferenceArgs.Logger.LogInformation("Starting restore preview");
            var restorePreviewResult = PreviewAddPackageReference(packageReferenceArgs).Result;
            packageReferenceArgs.Logger.LogInformation("Returned from restore preview");
        }

        public async Task<IReadOnlyList<RestoreResultPair>> PreviewAddPackageReference(PackageReferenceArgs packageReferenceArgs)
        {
            if (packageReferenceArgs == null)
            {
                throw new ArgumentNullException(nameof(packageReferenceArgs));
            }
            // 1. Get project dg file
            // 2. Run Restore Preview
            // 3. Process Restore Result
            // 4. Write to Project

            using (var dgFilePath = new TempFile(".dg"))
            {
                var dgSpecTask = GetProjectDependencyGraphAsync(packageReferenceArgs, dgFilePath, timeOut: 5000, recursive: true);

                // Set user agent and connection settings.
                ConfigureProtocol();

                //var graphLines = RestoreGraphItems;
                var providerCache = new RestoreCommandProvidersCache();

                using (var cacheContext = new SourceCacheContext())
                {
                    cacheContext.NoCache = false;
                    cacheContext.IgnoreFailedSources = true;

                    // Pre-loaded request provider containing the graph file
                    var providers = new List<IPreLoadedRestoreRequestProvider>();
                    var defaultSettings = Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null);
                    var sourceProvider = new CachingSourceProvider(new PackageSourceProvider(defaultSettings));

                    //wait for the GenerateDGSpecTask to finish
                    var dgSpec = await dgSpecTask;

                    if (dgSpec.Restore.Count < 1)
                    {
                        // Restore will fail if given no inputs, but here we should skip it.
                        return Enumerable.Empty<RestoreResultPair>().ToList();
                    }

                    // Add the new package into dgSpec
                    var project = dgSpec.Restore.FirstOrDefault();

                    var originalPackageSpec = dgSpec.GetProjectSpec(project);

                    // Create a copy to avoid modifying the original spec which may be shared.
                    var updatedPackageSpec = originalPackageSpec.Clone();

                    PackageSpecOperations.AddOrUpdateDependency(updatedPackageSpec, packageReferenceArgs.PackageIdentity);

                    providers.Add(new DependencyGraphSpecRequestProvider(providerCache, dgSpec));

                    var restoreContext = new RestoreArgs()
                    {
                        CacheContext = cacheContext,
                        LockFileVersion = LockFileFormat.Version,
                        DisableParallel = true,
                        Log = packageReferenceArgs.Logger,
                        MachineWideSettings = new XPlatMachineWideSetting(),
                        PreLoadedRequestProviders = providers,
                        CachingSourceProvider = sourceProvider
                    };

                    if (restoreContext.DisableParallel)
                    {
                        HttpSourceResourceProvider.Throttle = SemaphoreSlimThrottle.CreateBinarySemaphore();
                    }

                    var restoreRequests = await RestoreRunner.GetRequests(restoreContext);
                    var restoreResult = await RestoreRunner.RunWithoutCommit(restoreRequests, restoreContext);

                    var allFrameworks = updatedPackageSpec.TargetFrameworks
                                                          .Select(t => t.FrameworkName)
                                                          .Distinct()
                                                          .ToList();

                    var unsuccessfulFrameworks = restoreResult.Single()
                                                              .Result
                                                              .CompatibilityCheckResults
                                                              .Where(t => !t.Success)
                                                              .Select(t => t.Graph.Framework)
                                                              .Distinct()
                                                              .ToList();

                    var successfulFrameworks = allFrameworks.Except(unsuccessfulFrameworks)
                                                            .ToList();

                    return restoreResult;
                }
            }
        }

        private static void ConfigureProtocol()
        {
            // Set connection limit
            NetworkProtocolUtility.SetConnectionLimit();

            // Set user agent string used for network calls
            SetUserAgent();

            // This method has no effect on .NET Core.
            NetworkProtocolUtility.ConfigureSupportedSslProtocols();
        }

        private static void SetUserAgent()
        {
            var agent = "dotnet addref Task";

#if IS_CORECLR
            UserAgent.SetUserAgentString(new UserAgentStringBuilder(agent)
                .WithOSDescription(RuntimeInformation.OSDescription));
#else
            // OS description is set by default on Desktop
            UserAgent.SetUserAgentString(new UserAgentStringBuilder(agent));
#endif
        }

        public static async Task<DependencyGraphSpec> GetProjectDependencyGraphAsync(
            PackageReferenceArgs packageReferenceArgs,
            string dgFilePath,
            int timeOut,
            bool recursive)
        {
            var dotnetLocation = @"F:\paths\dotnet\dotnet.exe";//NuGetEnvironment.GetDotNetLocation();

            if (!File.Exists(dotnetLocation))
            {
                throw new Exception(
                    string.Format(CultureInfo.CurrentCulture, Strings.Error_DotnetNotFound));
            }
            var argumentBuilder = new StringBuilder($@" /t:GenerateRestoreGraphFile");

            // Set the msbuild verbosity level if specified
            var msbuildVerbosity = Environment.GetEnvironmentVariable("NUGET_RESTORE_MSBUILD_VERBOSITY");

            if (string.IsNullOrEmpty(msbuildVerbosity))
            {
                argumentBuilder.Append(" /v:q ");
            }
            else
            {
                argumentBuilder.Append($" /v:{msbuildVerbosity} ");
            }

            // pass dg file output path
            argumentBuilder.Append(" /p:RestoreGraphOutputPath=");
            AppendQuoted(argumentBuilder, dgFilePath);

            // Add all depenencies as top level restore projects if recursive is set
            if (recursive)
            {
                argumentBuilder.Append($" /p:RestoreRecursive=true ");
            }

            var processStartInfo = new ProcessStartInfo
            {
                UseShellExecute = false,
                FileName = dotnetLocation,
                Arguments = $"msbuild {argumentBuilder.ToString()}",
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            packageReferenceArgs.Logger.LogDebug($"{processStartInfo.FileName} {processStartInfo.Arguments}");

            using (var process = Process.Start(processStartInfo))
            {
                var errors = new StringBuilder();
                var errorTask = ConsumeStreamReaderAsync(process.StandardError, errors);
                var finished = process.WaitForExit(timeOut);
                if (!finished)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (Exception ex)
                    {
                        throw new Exception(string.Format(CultureInfo.CurrentCulture, Strings.Error_CannotKillDotnetMsBuild) + " : " +
                            ex.Message,
                            ex);
                    }

                    throw new Exception(string.Format(CultureInfo.CurrentCulture, Strings.Error_DotnetMsBuildTimedOut));
                }

                if (process.ExitCode != 0)
                {
                    await errorTask;
                    throw new Exception(errors.ToString());
                }
            }

            DependencyGraphSpec spec = null;

            if (File.Exists(dgFilePath))
            {
                spec = DependencyGraphSpec.Load(dgFilePath);
                File.Delete(dgFilePath);
            }
            else
            {
                spec = new DependencyGraphSpec();
            }

            return spec;
        }

        private static void AppendQuoted(StringBuilder builder, string targetPath)
        {
            builder
                .Append('"')
                .Append(targetPath)
                .Append('"');
        }

        private static async Task ConsumeStreamReaderAsync(StreamReader reader, StringBuilder lines)
        {
            await Task.Yield();

            string line;
            while ((line = await reader.ReadLineAsync()) != null)
            {
                lines.AppendLine(line);
            }
        }

        private class TempFile : IDisposable
        {
            private readonly string _filePath;

            /// <summary>
            /// Constructor. It creates an empty temp file under the temp directory / NuGet, with
            /// extension <paramref name="extension"/>.
            /// </summary>
            /// <param name="extension">The extension of the temp file.</param>
            public TempFile(string extension)
            {
                if (string.IsNullOrEmpty(extension))
                {
                    throw new ArgumentNullException(nameof(extension));
                }

                var tempDirectory = Path.Combine(Path.GetTempPath(), "NuGet-Scratch");

                Directory.CreateDirectory(tempDirectory);

                _filePath = Path.Combine(tempDirectory, Path.GetRandomFileName() + extension);

                if (!File.Exists(_filePath))
                {
                    try
                    {
                        File.Create(_filePath).Dispose();
                        // file is created successfully.
                        return;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(string.Format(CultureInfo.CurrentCulture, Strings.Error_FailedToCreateRandomFile) + " : " +
                                ex.Message,
                                ex);
                    }
                }
            }

            public static implicit operator string(TempFile f)
            {
                return f._filePath;
            }

            public void Dispose()
            {
                if (File.Exists(_filePath))
                {
                    try
                    {
                        File.Delete(_filePath);
                    }
                    catch
                    {
                    }
                }
            }
        }
    }
}