using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Dnx.Runtime.Common.CommandLine;
using NuGet.Configuration;

namespace NuGet.CommandLine.XPlat.Commands
{
    internal static class AddPackageReferenceCommand
    {
        public static void Register(CommandLineApplication app, Func<ILogger> getLogger)
        {
            app.Command("dotnet add pkg", addPkgRef =>
            {
                addPkgRef.Description = Strings.LocalsCommand_Description;
                addPkgRef.HelpOption(XPlatUtility.HelpOption);

                addPkgRef.Option(
                    CommandConstants.ForceEnglishOutputOption,
                    Strings.ForceEnglishOutput_Description,
                    CommandOptionType.NoValue);

                var clear = addPkgRef.Option(
                    "-c|--clear",
                    Strings.LocalsCommand_ClearDescription,
                    CommandOptionType.NoValue);

                var list = addPkgRef.Option(
                    "-l|--list",
                    Strings.LocalsCommand_ListDescription,
                    CommandOptionType.NoValue);

                var arguments = addPkgRef.Argument(
                    "Cache Location(s)",
                    Strings.LocalsCommand_ArgumentDescription,
                    multipleValues: false);

                addPkgRef.OnExecute(() =>
                {
                    var logger = getLogger();
                    var setting = Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null);
                    // Using both -clear and -list command options, or neither one of them, is not supported.
                    // We use MinArgs = 0 even though the first argument is required,
                    // to avoid throwing a command argument validation exception and
                    // immediately show usage help for this command instead.
                    if ((arguments.Values.Count < 1) || string.IsNullOrWhiteSpace(arguments.Values[0]))
                    {
                        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Strings.LocalsCommand_NoArguments));
                    }
                    else if (clear.HasValue() && list.HasValue())
                    {
                        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Strings.LocalsCommand_MultipleOperations));
                    }
                    else if (!clear.HasValue() && !list.HasValue())
                    {
                        throw new ArgumentException(string.Format(CultureInfo.CurrentCulture, Strings.LocalsCommand_NoOperation));
                    }
                    else
                    {
                        var localsArgs = new LocalsArgs(arguments.Values, setting, logger.LogInformation, logger.LogError, clear.HasValue(), list.HasValue());
                        var localsCommandRunner = new LocalsCommandRunner();
                        localsCommandRunner.ExecuteCommand(localsArgs);
                    }

                    return 0;
                });
            });
        }
    }