// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Bicep.Core.FileSystem;
using Bicep.Core.UnitTests.Assertions;
using Bicep.Core.UnitTests.Utils;
using FluentAssertions;
using FluentAssertions.Execution;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bicep.Cli.IntegrationTests
{
    [TestClass]
    public class DecompileCommandTests : TestBase
    {
        private const string ValidTemplate = @"{
                ""$schema"": ""https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#"",
                ""contentVersion"": ""1.0.0.0"",
                ""parameters"": {},
                ""variables"": {},
                ""resources"": [
                    {
                        ""type"": ""My.Rp/testType"",
                        ""apiVersion"": ""2020-01-01"",
                        ""name"": ""resName"",
                        ""location"": ""[resourceGroup().location]"",
                        ""properties"": {
                            ""prop1"": ""val1""
                        }
                    }
                ],
                ""outputs"": {}
            }";

        private const string InvalidTemplate = @"{
            ""$schema"": ""https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#"",
            ""contentVersion"": ""1.0.0.0"",
            ""parameters"": {},
            ""variables"": {},
            ""resources"": [
                {
                    ""type"": ""My.Rp/testType"",
                    ""apiVersion"": ""2020-01-01"",
                    ""name"": ""resName"",
                    ""properties"": {
                        ""cyclicDependency"": ""[reference(resourceId('My.Rp/testType', 'resName'))]""
                    }
                }
            ],
            ""outputs"": {}
        }";

        private const string ValidTemplateExpectedDecompilation = """
            resource res 'My.Rp/testType@2020-01-01' = {
              name: 'resName'
              location: resourceGroup().location
              properties: {
                prop1: 'val1'
              }
            }

            """;

        private const string InvalidTemplateExpectedDecompilation = """
            resource res 'My.Rp/testType@2020-01-01' = {
              name: 'resName'
              properties: {
                cyclicDependency: res.properties
              }
            }

            """;

        private const string EmptyTemplate = @"{
    ""$schema"": ""https://schema.management.azure.com/schemas/2019-04-01/deploymentTemplate.json#"",
    ""contentVersion"": ""1.0.0.0"",
    ""parameters"": {},
    ""variables"": {},
    ""resources"": [],
    ""outputs"": {},
    ""metadata"": {
        ""_generator"": {
            ""name"": ""bicep"",
            ""version"": ""dev"",
            ""templateHash"": ""<templateHash>""
        }
    }
}";

        private readonly string[] DecompilationDisclaimer =
        [
            "WARNING: Decompilation is a best-effort process, as there is no guaranteed mapping from ARM JSON to Bicep Template or Bicep Parameters.",
            "You may need to fix warnings and errors in the generated bicep/bicepparam file(s), or decompilation may fail entirely if an accurate conversion is not possible.",
            "If you would like to report any issues or inaccurate conversions, please see https://github.com/Azure/bicep/issues."
        ];


        [TestMethod]
        public async Task Decompile_EmptyTemplate_ShouldSucceed()
        {
            var (jsonPath, bicepPath) = Setup(TestContext);

            var (output, error, result) = await Bicep("decompile", jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);
            }

            File.ReadAllText(bicepPath).Should().BeEquivalentToIgnoringNewlines("\n");
        }

        [TestMethod]
        public async Task Decompile_ZeroFiles_ShouldFail_WithExpectedErrorMessage()
        {
            var (output, error, result) = await Bicep("decompile");

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.Should().Contain($"The input file path was not specified");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Decompile_WithNonExistentOutDir_ShouldCreateOutDir()
        {
            var (jsonPath, outputDir) = Setup(TestContext, ValidTemplate, outputDir: "outputDir");

            var (output, error, result) = await Bicep("decompile", "--outdir", outputDir, jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithErrors_ShouldFail_WithExpectedErrorMessage()
        {

            var (jsonPath, bicepPath) = Setup(TestContext, InvalidTemplate);

            var (output, error, result) = await Bicep("decompile", jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                error.AsLines().Should().Contain($"{bicepPath}(4,23) : Error BCP079: This expression is referencing its own declaration, which is not allowed. [https://aka.ms/bicep/core-diagnostics#BCP079]");
                result.Should().Be(1);
                File.ReadAllText(bicepPath).Should().BeEquivalentToIgnoringNewlines(InvalidTemplateExpectedDecompilation);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithNoErrors_ShouldSucceed()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, ValidTemplate);

            var (output, error, result) = await Bicep("decompile", jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);
                File.ReadAllText(bicepPath).Should().BeEquivalentToIgnoringNewlines(ValidTemplateExpectedDecompilation);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithErrors_ToStdout_ShouldFail_WithExpectedErrorMessage()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, InvalidTemplate);

            var (output, error, result) = await Bicep("decompile", "--stdout", jsonPath);

            using (new AssertionScope())
            {
                output.AsLines().Should().BeEquivalentTo(
                    "resource res 'My.Rp/testType@2020-01-01' = {",
                    "  name: 'resName'",
                    "  properties: {",
                    "    cyclicDependency: res.properties",
                    "  }",
                    "}");
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                error.AsLines().Should().Contain($"{bicepPath}(4,23) : Error BCP079: This expression is referencing its own declaration, which is not allowed. [https://aka.ms/bicep/core-diagnostics#BCP079]");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithErrors_ToStdout_ShouldSucceed_WithExpectedErrorMessage()
        {
            var (jsonPath, _) = Setup(TestContext, ValidTemplate);

            var (output, error, result) = await Bicep("decompile", "--stdout", jsonPath);

            using (new AssertionScope())
            {
                output.AsLines().Should().BeEquivalentTo(
                    "resource res 'My.Rp/testType@2020-01-01' = {",
                    "  name: 'resName'",
                    "  location: resourceGroup().location",
                    "  properties: {",
                    "    prop1: 'val1'",
                    "  }",
                    "}");
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithNoErrors_ToOutFile_ShouldSucceed()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, ValidTemplate, outputDir: "test.bicep");

            var (output, error, result) = await Bicep("decompile", "--outfile", bicepPath, jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);

                File.ReadAllText(bicepPath).Should().BeEquivalentToIgnoringNewlines(ValidTemplateExpectedDecompilation);
            }
        }

        [TestMethod]
        public async Task Decompile_FileWithNoErrors_ToOutDir_ShouldSucceed()
        {
            var (jsonPath, outputDir) = Setup(TestContext, ValidTemplate, outputDir: ".");

            var (output, error, result) = await Bicep("decompile", "--outdir", outputDir, jsonPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(0);

                var bicepFile = File.ReadAllText(Path.Combine(outputDir, "main.bicep"));
                bicepFile.Should().BeEquivalentToIgnoringNewlines(ValidTemplateExpectedDecompilation);
            }
        }

        [DataRow("DoesNotExist.json")]
        [DataRow("WrongDir/Fake.json")]
        [DataTestMethod]
        public async Task Decompile_InvalidInputPath_ShouldFail_WithExpectedErrorMessage(string badPath)
        {
            badPath = Path.GetFullPath(badPath);

            var (output, error, result) = await Bicep("decompile", badPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);

                var directoryExists = Directory.Exists(Path.GetDirectoryName(badPath));
                var fatalError = directoryExists
                    ? $"Could not find file '{badPath}'."
                    : $"Could not find a part of the path '{badPath}'.";

                error.AsLines().Should().Contain($@"{badPath}: Decompilation failed with fatal error ""{fatalError}""");
                result.Should().Be(1);
            }
        }

        [DataRow("DoesNotExist.json")]
        [DataRow("WrongDir/Fake.json")]
        [DataTestMethod]
        public async Task Decompile_InvalidInputPath_ToStdout_ShouldFail_WithExpectedErrorMessage(string badPath)
        {
            badPath = Path.GetFullPath(badPath);

            var (output, error, result) = await Bicep("decompile", "--stdout", badPath);

            using (new AssertionScope())
            {
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);

                var directoryExists = Directory.Exists(Path.GetDirectoryName(badPath));
                var fatalError = directoryExists
                    ? $"Could not find file '{badPath}'."
                    : $"Could not find a part of the path '{badPath}'.";

                error.AsLines().Should().Contain($@"{badPath}: Decompilation failed with fatal error ""{fatalError}""");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Decompile_LockedFile_ShouldFail()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, string.Empty, inputFile: "Empty.json");

            // ReSharper disable once ConvertToUsingDeclaration
            using (new FileStream(bicepPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // keep the output stream open while we attempt to write to it
                // this should force an access denied error
                var (output, error, result) = await Bicep("decompile", "--force", jsonPath);

                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                error.AsLines().Should().ContainMatch($"{jsonPath}: Decompilation failed with fatal error *");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Decompile_OutputFileExists_ShouldFail()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, string.Empty, inputFile: "OutputFileExists.json");

            // ReSharper disable once ConvertToUsingDeclaration
            using (new FileStream(bicepPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // keep the output stream open while we attempt to write to it
                // this should force an access denied error
                await Bicep("decompile", jsonPath);
                var (output, error, result) = await Bicep("decompile", jsonPath);

                output.Should().BeEmpty();
                error.AsLines().Should().ContainMatch($"The output file \"{bicepPath}\" already exists. Use --force to overwrite the existing file.");
                result.Should().Be(1);
            }
        }

        [TestMethod]
        public async Task Decompile_OutputFileExists_Force_ShouldSuccess()
        {
            var (jsonPath, bicepPath) = Setup(TestContext, string.Empty, inputFile: "OutputFileExistsForce.json");

            // ReSharper disable once ConvertToUsingDeclaration
            using (new FileStream(bicepPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None))
            {
                // keep the output stream open while we attempt to write to it
                // this should force an access denied error
                await Bicep("decompile", jsonPath);
                var (output, error, result) = await Bicep("decompile", "--force", jsonPath);
                output.Should().BeEmpty();
                error.AsLines().Should().Contain(DecompilationDisclaimer);
                result.Should().Be(1);

            }
        }

        private static (string jsonPath, string bicepPath) Setup(TestContext context, string template = EmptyTemplate, string? inputFile = null, string? outputDir = null)
        {
            var jsonPath = FileHelper.SaveResultFile(context, inputFile ?? "main.json", template);

            string bicepPath;

            if (outputDir is null)
            {
                bicepPath = PathHelper.GetBicepOutputPath(jsonPath);
            }
            else
            {
                bicepPath = FileHelper.GetResultFilePath(context, outputDir);
            }

            return (jsonPath, bicepPath);
        }
    }
}
