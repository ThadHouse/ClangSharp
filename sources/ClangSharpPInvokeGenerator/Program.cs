// Copyright (c) Microsoft and Contributors. All rights reserved. Licensed under the University of Illinois/NCSA Open Source License. See LICENSE.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Invocation;
using System.CommandLine.IO;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using ClangSharp.Interop;

namespace ClangSharp
{
    public class Program
    {
        private static RootCommand s_rootCommand;
        private static Option s_configOption;

        private static readonly (string Name, string Description)[] s_configOptions = new (string Name, string Description)[]
        {
            ("?, h, help", "Show help and usage information for -c, --config"),

            ("", ""),   // Codegen Options

            ("compatible-codegen", "Bindings should be generated with .NET Standard 2.0 compatibility. Setting this disables preview code generation."),
            ("latest-codegen", "Bindings should be generated for the latest stable version of .NET/C#. This is currently .NET 5/C# 9."),
            ("preview-codegen", "Bindings should be generated for the latest preview version of .NET/C#. This is currently .NET 6/C# 10."),

            ("", ""),   // File Options

            ("single-file", "Bindings should be generated to a single output file. This is the default."),
            ("multi-file", "Bindings should be generated so there is approximately one type per file."),

            ("", ""),   // Type Options

            ("unix-types", "Bindings should be generated assuming Unix defaults. This is the default on Unix platforms."),
            ("windows-types", "Bindings should be generated assuming Windows defaults. This is the default on Windows platforms."),

            ("", ""),   // Exclusion Options

            ("exclude-anonymous-field-helpers", "The helper ref properties generated for fields in nested anonymous structs and unions should not be generated."),
            ("exclude-com-proxies", "Types recognized as COM proxies should not have bindings generated. Thes are currently function declarations ending with _UserFree, _UserMarshal, _UserSize, _UserUnmarshal, _Proxy, or _Stub."),
            ("exclude-default-remappings", "Default remappings for well known types should not be added. This currently includes intptr_t, ptrdiff_t, size_t, and uintptr_t"),
            ("exclude-empty-records", "Bindings for records that contain no members should not be generated. These are commonly encountered for opaque handle like types such as HWND."),
            ("exclude-enum-operators", "Bindings for operators over enum types should not be generated. These are largely unnecessary in C# as the operators are available by default."),
            ("exclude-fnptr-codegen", "Generated bindings for latest or preview codegen should not use function pointers."),
            ("exclude-funcs-with-body", "Bindings for functions with bodies should not be generated."),
            ("preview-codegen-nint", "Generated bindings for latest or preview codegen should not use nint or nuint."),
            ("exclude-using-statics-for-enums", "Enum usages should be fully qualified and should not include a corresponding 'using static EnumName;'"),

            ("", ""),   // VTBL Options

            ("explicit-vtbls", "VTBLs should have an explicit type generated with named fields per entry."),
            ("implicit-vtbls", "VTBLs should be implicit to reduce metadata bloat. This is the current default"),

            ("", ""),   // Test Options

            ("generate-tests-nunit", "Basic tests validating size, blittability, and associated metadata should be generated for NUnit."),
            ("generate-tests-xunit", "Basic tests validating size, blittability, and associated metadata should be generated for XUnit."),

            ("", ""),   // Generation Options

            ("generate-aggressive-inlining", "[MethodImpl(MethodImplOptions.AggressiveInlining)] should be added to generated helper functions."),
            ("generate-cpp-attributes", "[CppAttributeList(\"\")] should be generated to document the encountered C++ attributes."),
            ("generate-macro-bindings", "Bindings for macro-definitions should be generated. This currently only works with value like macros and not function-like ones."),
            ("generate-native-inheritance-attribute", "[NativeInheritance(\"\")] attribute should be generated to document the encountered C++ base type."),
            ("generate-vtbl-index-attribute", "[VtblIndex(#)] attribute should be generated to document the underlying VTBL index for a helper method."),

            ("", ""),   // Logging Options

            ("log-exclusions", "A list of excluded declaration types should be generated. This will also log if the exclusion was due to an exact or partial match."),
            ("log-potential-typedef-remappings", "A list of potential typedef remappings should be generated. This can help identify missing remappings."),
            ("log-visited-files", "A list of the visited files should be generated. This can help identify traversal issues."),
        };

        public static async Task<int> Main(params string[] args)
        {
            s_rootCommand = new RootCommand("ClangSharp P/Invoke Binding Generator")
            {
                Handler = CommandHandler.Create(typeof(Program).GetMethod(nameof(Run)))
            };

            AddAdditionalOption(s_rootCommand);
            AddConfigOption(s_rootCommand);
            AddDefineMacroOption(s_rootCommand);
            AddExcludeOption(s_rootCommand);
            AddFileOption(s_rootCommand);
            AddFileDirectoryOption(s_rootCommand);
            AddHeaderOption(s_rootCommand);
            AddIncludeDirectoryOption(s_rootCommand);
            AddLanguageOption(s_rootCommand);
            AddLibraryOption(s_rootCommand);
            AddMethodClassNameOption(s_rootCommand);
            AddNamespaceOption(s_rootCommand);
            AddOutputOption(s_rootCommand);
            AddPrefixStripOption(s_rootCommand);
            AddRemapOption(s_rootCommand);
            AddStdOption(s_rootCommand);
            AddTestOutputOption(s_rootCommand);
            AddTraverseOption(s_rootCommand);
            AddWithAttributeOption(s_rootCommand);
            AddWithCallConvOption(s_rootCommand);
            AddWithLibraryPathOption(s_rootCommand);
            AddWithSetLastErrorOption(s_rootCommand);
            AddWithTypeOption(s_rootCommand);
            AddWithUsingOption(s_rootCommand);
            AddOutputModeOption(s_rootCommand);

            return await s_rootCommand.InvokeAsync(args);
        }

        public static int Run(InvocationContext context)
        {
            var additionalArgs = context.ParseResult.ValueForOption<string[]>("--additional");
            var configSwitches = context.ParseResult.ValueForOption<string[]>("--config");
            var defineMacros = context.ParseResult.ValueForOption<string[]>("--define-macro");
            var excludedNames = context.ParseResult.ValueForOption<string[]>("--exclude");
            var files = context.ParseResult.ValueForOption<string[]>("--file");
            var fileDirectory = context.ParseResult.ValueForOption<string>("--file-directory");
            var headerFile = context.ParseResult.ValueForOption<string>("--headerFile");
            var includeDirectories = context.ParseResult.ValueForOption<string[]>("--include-directory");
            var language = context.ParseResult.ValueForOption<string>("--language");
            var libraryPath = context.ParseResult.ValueForOption<string>("--libraryPath");
            var methodClassName = context.ParseResult.ValueForOption<string>("--methodClassName");
            var methodPrefixToStrip = context.ParseResult.ValueForOption<string>("--prefixStrip");
            var namespaceName = context.ParseResult.ValueForOption<string>("--namespace");
            var outputLocation = context.ParseResult.ValueForOption<string>("--output");
            var remappedNameValuePairs = context.ParseResult.ValueForOption<string[]>("--remap");
            var std = context.ParseResult.ValueForOption<string>("--std");
            var testOutputLocation = context.ParseResult.ValueForOption<string>("--test-output");
            var traversalNames = context.ParseResult.ValueForOption<string[]>("--traverse");
            var withAttributeNameValuePairs = context.ParseResult.ValueForOption<string[]>("--with-attribute");
            var withCallConvNameValuePairs = context.ParseResult.ValueForOption<string[]>("--with-callconv");
            var withLibraryPathNameValuePairs = context.ParseResult.ValueForOption<string[]>("--with-librarypath");
            var withSetLastErrors = context.ParseResult.ValueForOption<string[]>("--with-setlasterror");
            var withTypeNameValuePairs = context.ParseResult.ValueForOption<string[]>("--with-type");
            var withUsingNameValuePairs = context.ParseResult.ValueForOption<string[]>("--with-using");
            var outputMode = context.ParseResult.ValueForOption<PInvokeGeneratorOutputMode>("--output-mode");

            var errorList = new List<string>();

            if (!files.Any())
            {
                errorList.Add("Error: No input C/C++ files provided. Use --file or -f");
            }

            if (string.IsNullOrWhiteSpace(namespaceName))
            {
                errorList.Add("Error: No namespace provided. Use --namespace or -n");
            }

            if (string.IsNullOrWhiteSpace(outputLocation))
            {
                errorList.Add("Error: No output file location provided. Use --output or -o");
            }

            ParseKeyValuePairs(remappedNameValuePairs, errorList, out Dictionary<string, string> remappedNames);
            ParseKeyValuePairs(withAttributeNameValuePairs, errorList, out Dictionary<string, IReadOnlyList<string>> withAttributes);
            ParseKeyValuePairs(withCallConvNameValuePairs, errorList, out Dictionary<string, string> withCallConvs);
            ParseKeyValuePairs(withLibraryPathNameValuePairs, errorList, out Dictionary<string, string> withLibraryPath);
            ParseKeyValuePairs(withTypeNameValuePairs, errorList, out Dictionary<string, string> withTypes);
            ParseKeyValuePairs(withUsingNameValuePairs, errorList, out Dictionary<string, IReadOnlyList<string>> withUsings);

            var configOptions = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? PInvokeGeneratorConfigurationOptions.None : PInvokeGeneratorConfigurationOptions.GenerateUnixTypes;
            var printConfigHelp = false;

            foreach (var configSwitch in configSwitches)
            {
                switch (configSwitch)
                {
                    case "?":
                    case "h":
                    case "help":
                    {
                        printConfigHelp = true;
                        break;
                    }

                    // Codegen Options

                    case "compatible-codegen":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateCompatibleCode;
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GeneratePreviewCode;
                        break;
                    }

                    case "latest-codegen":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GenerateCompatibleCode;
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GeneratePreviewCode;
                        break;
                    }

                    case "preview-codegen":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GenerateCompatibleCode;
                        configOptions |= PInvokeGeneratorConfigurationOptions.GeneratePreviewCode;
                        break;
                    }

                    // File Options

                    case "single-file":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GenerateMultipleFiles;
                        break;
                    }

                    case "multi-file":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateMultipleFiles;
                        break;
                    }

                    // Type Options

                    case "unix-types":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateUnixTypes;
                        break;
                    }

                    case "windows-types":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GenerateUnixTypes;
                        break;
                    }

                    // Exclusion Options

                    case "exclude-anonymous-field-helpers":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeAnonymousFieldHelpers;
                        break;
                    }

                    case "exclude-com-proxies":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeComProxies;
                        break;
                    }

                    case "exclude-default-remappings":
                    case "no-default-remappings":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.NoDefaultRemappings;
                        break;
                    }

                    case "exclude-empty-records":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeEmptyRecords;
                        break;
                    }

                    case "exclude-enum-operators":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeEnumOperators;
                        break;
                    }

                    case "exclude-fnptr-codegen":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeFnptrCodegen;
                        break;
                    }

                    case "exclude-funcs-with-body":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeFunctionsWithBody;
                        break;
                    }

                    case "exclude-nint-codegen":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.ExcludeNIntCodegen;
                        break;
                    }

                    case "exclude-using-statics-for-enums":
                    case "dont-use-using-statics-for-enums":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.DontUseUsingStaticsForEnums;
                        break;
                    }

                    // VTBL Options

                    case "explicit-vtbls":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateExplicitVtbls;
                        break;
                    }

                    case "implicit-vtbls":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.GenerateExplicitVtbls;
                        break;
                    }

                    // Test Options

                    case "generate-tests-nunit":
                    {
                        if (string.IsNullOrWhiteSpace(testOutputLocation))
                        {
                            errorList.Add("Error: No test output file location provided. Use --test-output or -to");
                        }

                        if (configOptions.HasFlag(PInvokeGeneratorConfigurationOptions.GenerateTestsXUnit))
                        {
                            errorList.Add("Cannot generate both NUnit and XUnit tests.");
                        }
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateTestsNUnit;
                        break;
                    }

                    case "generate-tests-xunit":
                    {
                        if (string.IsNullOrWhiteSpace(testOutputLocation))
                        {
                            errorList.Add("Error: No test output file location provided. Use --test-output or -to");
                        }

                        if (configOptions.HasFlag(PInvokeGeneratorConfigurationOptions.GenerateTestsNUnit))
                        {
                            errorList.Add("Cannot generate both NUnit and XUnit tests.");
                        }
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateTestsXUnit;
                        break;
                    }

                    // Generation Options

                    case "generate-aggressive-inlining":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateAggressiveInlining;
                        break;
                    }

                    case "generate-cpp-attributes":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateCppAttributes;
                        break;
                    }

                    case "generate-macro-bindings":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateMacroBindings;
                        break;
                    }

                    case "generate-native-inheritance-attribute":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateNativeInheritanceAttribute;
                        break;
                    }

                    case "generate-vtbl-index-attribute":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.GenerateVtblIndexAttribute;
                        break;
                    }

                    // Logging Options

                    case "log-exclusions":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.LogExclusions;
                        break;
                    }

                    case "log-potential-typedef-remappings":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.LogPotentialTypedefRemappings;
                        break;
                    }

                    case "log-visited-files":
                    {
                        configOptions |= PInvokeGeneratorConfigurationOptions.LogVisitedFiles;
                        break;
                    }

                    // Legacy Options

                    case "default-remappings":
                    {
                        configOptions &= ~PInvokeGeneratorConfigurationOptions.NoDefaultRemappings;
                        break;
                    }

                    default:
                    {
                        errorList.Add($"Error: Unrecognized config switch: {configSwitch}.");
                        break;
                    }
                }
            }

            if (!string.IsNullOrWhiteSpace(testOutputLocation) && !configOptions.HasFlag(PInvokeGeneratorConfigurationOptions.GenerateTestsNUnit) && !configOptions.HasFlag(PInvokeGeneratorConfigurationOptions.GenerateTestsXUnit))
            {
                errorList.Add("Error: No test format provided. Use --config generate-tests-nunit or --config generate-tests-xunit");
            }

            if (printConfigHelp)
            {
                var helpBuilder = new CustomHelpBuilder(context.Console);
                helpBuilder.Write(s_configOption);

                context.Console.Out.WriteLine();
                context.Console.Out.WriteLine();

                helpBuilder.Write(s_configOptions);

                return -1;
            }

            if (errorList.Any())
            {
                foreach (var error in errorList)
                {
                    context.Console.Error.Write(error);
                    context.Console.Error.Write(Environment.NewLine);
                }
                context.Console.Error.Write(Environment.NewLine);

                new HelpBuilder(context.Console).Write(s_rootCommand);
                return -1;
            }

            var clangCommandLineArgs = new string[]
            {
                $"--language={language}",               // Treat subsequent input files as having type <language>
                $"--std={std}",                         // Language standard to compile for
                "-Wno-pragma-once-outside-header"       // We are processing files which may be header files
            };

            clangCommandLineArgs = clangCommandLineArgs.Concat(includeDirectories.Select(x => "--include-directory=" + x)).ToArray();
            clangCommandLineArgs = clangCommandLineArgs.Concat(defineMacros.Select(x => "--define-macro=" + x)).ToArray();
            clangCommandLineArgs = clangCommandLineArgs.Concat(additionalArgs).ToArray();

            var translationFlags = CXTranslationUnit_Flags.CXTranslationUnit_None;

            translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_IncludeAttributedTypes;               // Include attributed types in CXType
            translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_VisitImplicitAttributes;              // Implicit attributes should be visited

            var config = new PInvokeGeneratorConfiguration(libraryPath, namespaceName, outputLocation, testOutputLocation, outputMode, configOptions, excludedNames, headerFile, methodClassName, methodPrefixToStrip, remappedNames, traversalNames, withAttributes, withCallConvs, withLibraryPath, withSetLastErrors, withTypes, withUsings);

            if (config.GenerateMacroBindings)
            {
                translationFlags |= CXTranslationUnit_Flags.CXTranslationUnit_DetailedPreprocessingRecord;
            }

            int exitCode = 0;

            using (var pinvokeGenerator = new PInvokeGenerator(config))
            {
                foreach (var file in files)
                {
                    var filePath = Path.Combine(fileDirectory, file);

                    var translationUnitError = CXTranslationUnit.TryParse(pinvokeGenerator.IndexHandle, filePath, clangCommandLineArgs, Array.Empty<CXUnsavedFile>(), translationFlags, out CXTranslationUnit handle);
                    var skipProcessing = false;

                    if (translationUnitError != CXErrorCode.CXError_Success)
                    {
                        Console.WriteLine($"Error: Parsing failed for '{filePath}' due to '{translationUnitError}'.");
                        skipProcessing = true;
                    }
                    else if (handle.NumDiagnostics != 0)
                    {
                        Console.WriteLine($"Diagnostics for '{filePath}':");

                        for (uint i = 0; i < handle.NumDiagnostics; ++i)
                        {
                            using var diagnostic = handle.GetDiagnostic(i);

                            Console.Write("    ");
                            Console.WriteLine(diagnostic.Format(CXDiagnostic.DefaultDisplayOptions).ToString());

                            skipProcessing |= (diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Error);
                            skipProcessing |= (diagnostic.Severity == CXDiagnosticSeverity.CXDiagnostic_Fatal);
                        }
                    }

                    if (skipProcessing)
                    {
                        Console.WriteLine($"Skipping '{filePath}' due to one or more errors listed above.");
                        Console.WriteLine();

                        exitCode = -1;
                        continue;
                    }

                    try
                    {
                        using var translationUnit = TranslationUnit.GetOrCreate(handle);
                        Console.WriteLine($"Processing '{filePath}'");

                        pinvokeGenerator.GenerateBindings(translationUnit, filePath, clangCommandLineArgs, translationFlags);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e);
                    }
                }

                if (pinvokeGenerator.Diagnostics.Count != 0)
                {
                    Console.WriteLine("Diagnostics for binding generation:");

                    foreach (var diagnostic in pinvokeGenerator.Diagnostics)
                    {
                        Console.Write("    ");
                        Console.WriteLine(diagnostic);

                        if (diagnostic.Level == DiagnosticLevel.Warning)
                        {
                            if (exitCode >= 0)
                            {
                                exitCode++;
                            }
                        }
                        else if (diagnostic.Level == DiagnosticLevel.Error)
                        {
                            if (exitCode >= 0)
                            {
                                exitCode = -1;
                            }
                            else
                            {
                                exitCode--;
                            }
                        }
                    }
                }
            }

            return exitCode;
        }

        private static void ParseKeyValuePairs(string[] keyValuePairs, List<string> errorList, out Dictionary<string, string> result)
        {
            result = new Dictionary<string, string>();

            foreach (var keyValuePair in keyValuePairs)
            {
                var parts = keyValuePair.Split('=');

                if (parts.Length != 2)
                {
                    errorList.Add($"Error: Invalid key/value pair argument: {keyValuePair}. Expected 'name=value'");
                    continue;
                }

                var key = parts[0].TrimEnd();

                if (result.ContainsKey(key))
                {
                    errorList.Add($"Error: A key with the given name already exists: {key}. Existing: {result[key]}");
                    continue;
                }

                result.Add(key, parts[1].TrimStart());
            }
        }

        private static void ParseKeyValuePairs(string[] keyValuePairs, List<string> errorList, out Dictionary<string, IReadOnlyList<string>> result)
        {
            result = new Dictionary<string, IReadOnlyList<string>>();

            foreach (var keyValuePair in keyValuePairs)
            {
                var parts = keyValuePair.Split('=');

                if (parts.Length != 2)
                {
                    errorList.Add($"Error: Invalid key/value pair argument: {keyValuePair}. Expected 'name=value'");
                    continue;
                }

                var key = parts[0].TrimEnd();

                if (!result.ContainsKey(key))
                {
                    result.Add(key, new List<string>());
                }

                var list = (List<string>)result[key];
                list.Add(parts[1].TrimStart());
            }
        }

        private static void AddAdditionalOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--additional", "-a" }, "An argument to pass to Clang when parsing the input files.")
            {
                Argument = new Argument("<arg>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddConfigOption(RootCommand rootCommand)
        {
            if (s_configOption is null)
            {
                s_configOption = new Option(new string[] { "--config", "-c" }, "A configuration option that controls how the bindings are generated. Specify 'help' to see the available options.")
                {
                    Argument = new Argument("<arg>")
                    {
                        ArgumentType = typeof(string),
                        Arity = ArgumentArity.OneOrMore,
                    }
                };
                s_configOption.Argument.SetDefaultValue(Array.Empty<string>());
            }
            rootCommand.AddOption(s_configOption);
        }

        private static void AddDefineMacroOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--define-macro", "-D" }, "Define <macro> to <value> (or 1 if <value> omitted).")
            {
                Argument = new Argument("<macro>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddExcludeOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--exclude", "-e" }, "A declaration name to exclude from binding generation.")
            {
                Argument = new Argument("<name>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddFileOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--file", "-f" }, "A file to parse and generate bindings for.")
            {
                Argument = new Argument("<file>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddFileDirectoryOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--file-directory", "-F" }, "The base path for files to parse.")
            {
                Argument = new Argument("<directory>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddHeaderOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--headerFile", "-h" }, "A file which contains the header to prefix every generated file with.")
            {
                Argument = new Argument("<file>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddIncludeDirectoryOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--include-directory", "-I" }, "Add directory to include search path.")
            {
                Argument = new Argument("<arg>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddLanguageOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--language", "-x" }, "Treat subsequent input files as having type <language>.")
            {
                Argument = new Argument("<arg>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue("c++");

            rootCommand.AddOption(option);
        }

        private static void AddLibraryOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--libraryPath", "-l" }, "The string to use in the DllImport attribute used when generating bindings.")
            {
                Argument = new Argument("<dllName>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddMethodClassNameOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--methodClassName", "-m" }, "The name of the static class that will contain the generated method bindings.")
            {
                Argument = new Argument("<className>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue("Methods");

            rootCommand.AddOption(option);
        }

        private static void AddNamespaceOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--namespace", "-n" }, "The namespace in which to place the generated bindings.")
            {
                Argument = new Argument("<namespace>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddOutputOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--output", "-o" }, "The output location to write the generated bindings to.")
            {
                Argument = new Argument("<file>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddPrefixStripOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--prefixStrip", "-p" }, "The prefix to strip from the generated method bindings.")
            {
                Argument = new Argument("<prefix>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddRemapOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--remap", "-r" }, "A declaration name to be remapped to another name during binding generation.")
            {
                Argument = new Argument("<name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddStdOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--std", "-std" }, "Language standard to compile for.")
            {
                Argument = new Argument("<arg>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue("c++17");

            rootCommand.AddOption(option);
        }

        private static void AddTestOutputOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--test-output", "-to" }, "The output location to write the generated tests to.")
            {
                Argument = new Argument("<file>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.ExactlyOne,
                }
            };
            option.Argument.SetDefaultValue(string.Empty);

            rootCommand.AddOption(option);
        }

        private static void AddTraverseOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--traverse", "-t" }, "A file name included either directly or indirectly by -f that should be traversed during binding generation.")
            {
                Argument = new Argument("<name>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithAttributeOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-attribute", "-wa" }, "An attribute to be added to the given remapped declaration name during binding generation.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithCallConvOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-callconv", "-wcc" }, "A calling convention to be used for the given declaration during binding generation.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithLibraryPathOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-librarypath", "-wlb" }, "A library path to be used for the given declaration during binding generation.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithSetLastErrorOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-setlasterror", "-wsle" }, "Add the SetLastError=true modifier to a given DllImport or UnmanagedFunctionPointer.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithTypeOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-type", "-wt" }, "A type to be used for the given enum declaration during binding generation.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddWithUsingOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--with-using", "-wu" }, "A using directive to be included for the given remapped declaration name during binding generation.")
            {
                Argument = new Argument("<remapped-name>=<value>")
                {
                    ArgumentType = typeof(string),
                    Arity = ArgumentArity.OneOrMore,
                }
            };
            option.Argument.SetDefaultValue(Array.Empty<string>());

            rootCommand.AddOption(option);
        }

        private static void AddOutputModeOption(RootCommand rootCommand)
        {
            var option = new Option(new string[] { "--output-mode", "-om" }, "The mode describing how the information collected from the headers are presented in the resultant bindings.")
            {
                Argument = new Argument("<arg>")
                {
                    ArgumentType = typeof(PInvokeGeneratorOutputMode),
                    Arity = ArgumentArity.ExactlyOne
                }
            };
            option.Argument.SetDefaultValue(PInvokeGeneratorOutputMode.CSharp);

            rootCommand.AddOption(option);
        }
    }
}
