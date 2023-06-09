﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using System.IO;
using System.Xml;
using System.Linq;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Framework;
using Microsoft.Build.Construction;
using Microsoft.Build.Execution;
using Microsoft.Build.Shared;
using Microsoft.Build.Unittest;

using Project = Microsoft.Build.Evaluation.Project;
using ProjectCollection = Microsoft.Build.Evaluation.ProjectCollection;
using Toolset = Microsoft.Build.Evaluation.Toolset;

using InternalUtilities = Microsoft.Build.Internal.Utilities;

using XMakeElements = Microsoft.Build.Shared.XMakeElements;
using ResourceUtilities = Microsoft.Build.Shared.ResourceUtilities;
using InvalidProjectFileException = Microsoft.Build.Exceptions.InvalidProjectFileException;
using FrameworkLocationHelper = Microsoft.Build.Shared.FrameworkLocationHelper;

namespace Microsoft.Build.UnitTests.Construction
{
    [TestClass]
    public class SolutionProjectGenerator_Tests
    {
        private string _originalVisualStudioVersion = null;

        [TestInitialize]
        public void Setup()
        {
            // Save off the value for use during cleanup
            _originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVersion");
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Need to make sure the environment is cleared up for later tests
            Environment.SetEnvironmentVariable("VisualStudioVersion", _originalVisualStudioVersion);
        }

        /// <summary>
        /// Verify the AddNewErrorWarningMessageElement method
        /// </summary>
        [TestMethod]
        public void AddNewErrorWarningMessageElement()
        {
            MockLogger logger = new MockLogger();

            /**
             * <Project DefaultTargets=`Build` ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
             *   <Target Name=`Build`>
             *   </Target>
             * </Project
             */

            ProjectRootElement projectXml = ProjectRootElement.Create();
            ProjectTargetElement target = projectXml.AddTarget("Build");
            projectXml.DefaultTargets = "Build";
            projectXml.ToolsVersion = ObjectModelHelpers.MSBuildDefaultToolsVersion;

            SolutionProjectGenerator.AddErrorWarningMessageElement(target, XMakeElements.message, true, "SolutionVenusProjectNoClean");
            SolutionProjectGenerator.AddErrorWarningMessageElement(target, XMakeElements.warning, true, "SolutionParseUnknownProjectType", "proj1.csproj");
            SolutionProjectGenerator.AddErrorWarningMessageElement(target, XMakeElements.error, true, "SolutionInvalidSolutionConfiguration");

            Project project = new Project(projectXml);

            project.Build(logger);

            string code = null;
            string keyword = null;
            string text = ResourceUtilities.FormatResourceString(out code, out keyword, "SolutionParseUnknownProjectType", "proj1.csproj");

            // check the error event
            Assert.AreEqual(1, logger.Warnings.Count);
            BuildWarningEventArgs warning = logger.Warnings[0];

            Assert.AreEqual(text, warning.Message);
            Assert.AreEqual(code, warning.Code);
            Assert.AreEqual(keyword, warning.HelpKeyword);

            code = null;
            keyword = null;
            text = ResourceUtilities.FormatResourceString(out code, out keyword, "SolutionInvalidSolutionConfiguration");

            // check the warning event
            Assert.AreEqual(1, logger.Errors.Count);
            DialogWindowEditorToStringValueConverter error = logger.Errors[0];

            Assert.AreEqual(text, error.Message);
            Assert.AreEqual(code, error.Code);
            Assert.AreEqual(keyword, error.HelpKeyword);

            code = null;
            keyword = null;
            text = ResourceUtilities.FormatResourceString(out code, out keyword, "SolutionVenusProjectNoClean");

            // check the message event
            Assert.IsTrue(logger.FullLog.Contains(text), "Log should contain the regular message");
        }

        /// <summary>
        /// Test to make sure we properly set the ToolsVersion attribute on the in-memory project based
        /// on the Solution File Format Version.
        /// </summary>
        [TestMethod]
        public void EmitToolsVersionAttributeToInMemoryProject9()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV35 == null)
            {
                // ".NET Framework 3.5 is required to be installed for this test, but it is not installed.");
                return;
            }

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, "3.5", new DefaultLicenseValidator(0, 0, 0, 0), null);

            Assert.AreEqual("3.5", instances[0].ToolsVersion);
        }

        /// <summary>
        /// Test to make sure we properly set the ToolsVersion attribute on the in-memory project based
        /// on the Solution File Format Version.
        /// </summary>
        [TestMethod]
        public void EmitToolsVersionAttributeToInMemoryProject10()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV35 == null)
            {
                // ".NET Framework 3.5 is required to be installed for this test, but it is not installed.");
                return;
            }

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 10.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, "3.5", new DefaultLicenseValidator(0, 0, 0, 0), null);

            Assert.AreEqual("3.5", instances[0].ToolsVersion);
        }

        /// <summary>
        /// Test to make sure that if the solution file version doesn't map to a sub-toolset version, we won't try 
        /// to force it to be used.  
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void DefaultSubToolsetIfSolutionVersionSubToolsetDoesntExist()
        {
            Environment.SetEnvironmentVariable("VisualStudioVersion", null);

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 10.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

            Assert.AreEqual(ObjectModelHelpers.MSBuildDefaultToolsVersion, instances[0].ToolsVersion);

            Toolset t = ProjectCollection.GlobalProjectCollection.GetToolset(instances[0].ToolsVersion);

            Assert.AreEqual(t.DefaultSubToolsetVersion, instances[0].SubToolsetVersion);
            Assert.AreEqual(t.DefaultSubToolsetVersion, instances[0].GetPropertyValue("VisualStudioVersion"));
        }

        /// <summary>
        /// Test to make sure that if the solution version corresponds to an existing sub-toolset version, 
        /// barring other factors that might override, the sub-toolset will be based on the solution version. 
        /// </summary>
        [TestMethod]
        public void SubToolsetSetBySolutionVersion()
        {
            Environment.SetEnvironmentVariable("VisualStudioVersion", null);

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 12.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

            Assert.AreEqual(ObjectModelHelpers.MSBuildDefaultToolsVersion, instances[0].ToolsVersion);

            // being cautious -- we can't expect the sub-toolset to be picked if it doesn't exist in the first place
            if (instances[0].Toolset.SubToolsets.ContainsKey("11.0"))
            {
                Assert.AreEqual("11.0", instances[0].SubToolsetVersion);
                Assert.AreEqual("11.0", instances[0].GetPropertyValue("VisualStudioVersion"));
            }
        }

        /// <summary>
        /// Test to make sure that even if the solution version corresponds to an existing sub-toolset version, 
        /// </summary>
        [TestMethod]
        public void SolutionBasedSubToolsetVersionOverriddenByEnvironment()
        {
            Environment.SetEnvironmentVariable("VisualStudioVersion", "ABC");

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 12.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

            Assert.AreEqual(ObjectModelHelpers.MSBuildDefaultToolsVersion, instances[0].ToolsVersion);
            Assert.AreEqual("ABC", instances[0].SubToolsetVersion);
            Assert.AreEqual("ABC", instances[0].GetPropertyValue("VisualStudioVersion"));
        }


        /// <summary>
        /// Test to make sure that even if the solution version corresponds to an existing sub-toolset version
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void SolutionPassesSubToolsetToChildProjects2()
        {
            string classLibraryContentsToolsV4 = ObjectModelHelpers.CleanupFileContents(
                    @"
                        <Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns='msbuildnamespace'>
                            <Target Name='Build'>
                                <Message Text='.[$(VisualStudioVersion)]. .[$(MSBuildToolsVersion)].' />
                            </Target>
                        </Project>
                    ");

            string classLibraryContentsToolsV12 = ObjectModelHelpers.CleanupFileContents(
                    @"
                        <Project ToolsVersion=""msbuilddefaulttoolsversion"" DefaultTargets=""Build"" xmlns='msbuildnamespace'>
                            <Target Name='Build'>
                                <Message Text='.[$(VisualStudioVersion)]. .[$(MSBuildToolsVersion)].' />
                            </Target>
                        </Project>
                    ");

            string solutionFilePreambleV11 =
                    @"
                        Microsoft Visual Studio Solution File, Format Version 12.00
                        # Visual Studio Dev11
                     ";

            string solutionFilePreambleV12 =
                    @"
                        Microsoft Visual Studio Solution File, Format Version 12.00
                        # Visual Studio Dev11
                        VisualStudioVersion = 12.0.20311.0 VSPRO_PLATFORM
                        MinimumVisualStudioVersion = 10.0.40219.1
                     ";

            string solutionBodySingleProjectContents =
                    @"

                        Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ClassLibrary1"", ""ClassLibrary1.csproj"", ""{6185CC21-BE89-448A-B3C0-D1C27112E595}""
                        EndProject
                        Global
                            GlobalSection(SolutionConfigurationPlatforms) = preSolution
                                Debug|Mixed Platforms = Debug|Mixed Platforms
                                Release|Any CPU = Release|Any CPU
                            EndGlobalSection
                            GlobalSection(ProjectConfigurationPlatforms) = postSolution
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                            EndGlobalSection
                        EndGlobal
                    ";

            string solutionBodyMultipleProjectsContents =
                @"
                    Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ClassLibrary1"", ""ClassLibrary1.csproj"", ""{A437DBE9-DCAA-46D8-9D80-A50EDB2244FD}""
                    EndProject
                    Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ClassLibrary2"", ""ClassLibrary2.csproj"", ""{84AA5584-4B0F-41DE-95AA-589E1447EDA0}""
                    EndProject
                    Global
	                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
		                    Debug|Any CPU = Debug|Any CPU
		                    Release|Any CPU = Release|Any CPU
	                    EndGlobalSection
	                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
		                    {A437DBE9-DCAA-46D8-9D80-A50EDB2244FD}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		                    {A437DBE9-DCAA-46D8-9D80-A50EDB2244FD}.Debug|Any CPU.Build.0 = Debug|Any CPU
		                    {A437DBE9-DCAA-46D8-9D80-A50EDB2244FD}.Release|Any CPU.ActiveCfg = Release|Any CPU
		                    {A437DBE9-DCAA-46D8-9D80-A50EDB2244FD}.Release|Any CPU.Build.0 = Release|Any CPU
		                    {84AA5584-4B0F-41DE-95AA-589E1447EDA0}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		                    {84AA5584-4B0F-41DE-95AA-589E1447EDA0}.Debug|Any CPU.Build.0 = Debug|Any CPU
		                    {84AA5584-4B0F-41DE-95AA-589E1447EDA0}.Release|Any CPU.ActiveCfg = Release|Any CPU
		                    {84AA5584-4B0F-41DE-95AA-589E1447EDA0}.Release|Any CPU.Build.0 = Release|Any CPU
	                    EndGlobalSection
	                    GlobalSection(SolutionProperties) = preSolution
		                    HideSolutionNode = FALSE
	                    EndGlobalSection
                    EndGlobal
                ";


            string solutionFileContentsDev11 = solutionFilePreambleV11 + solutionBodySingleProjectContents;
            string solutionFileContentsDev12 = solutionFilePreambleV12 + solutionBodySingleProjectContents;

            string[] solutions = { solutionFileContentsDev11, solutionFileContentsDev12, solutionFileContentsDev12 };
            string[] projects = { classLibraryContentsToolsV4, classLibraryContentsToolsV4, classLibraryContentsToolsV12 };
            string[] logoutputs = { ".[11.0]. .[4.0].", ".[11.0]. .[4.0].", String.Format(".[{0}]. .[{0}].", ObjectModelHelpers.MSBuildDefaultToolsVersion) };

            string previousLegacyEnvironmentVariable = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                for (int i = 0; i < solutions.Length; i++)
                {
                    string solutionFile = ObjectModelHelpers.CreateFileInTempProjectDirectory("Foo.sln", solutions[i]);
                    string projectFile = ObjectModelHelpers.CreateFileInTempProjectDirectory("ClassLibrary1.csproj", projects[i]);
                    SolutionFile sp = new SolutionFile();

                    sp.FullPath = solutionFile;
                    sp.ParseSolutionFile();

                    ProjectInstance[] instances = SolutionProjectGenerator.Generate(sp, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

                    MockLogger logger = new MockLogger();
                    List<ILogger> loggers = new List<ILogger>(1);
                    loggers.Add(logger);

                    instances[0].Build(loggers);
                    logger.AssertLogContains(logoutputs[i]);
                }

                // Test Dev 12 sln and mixed v4.0 and v12.0 projects
                string solutionFileContentsDev12MultipleProjects = solutionFilePreambleV12 + solutionBodyMultipleProjectsContents;

                string solutionFileMultipleProjects = ObjectModelHelpers.CreateFileInTempProjectDirectory("Foo.sln", solutionFileContentsDev12MultipleProjects);
                string projectFileV4 = ObjectModelHelpers.CreateFileInTempProjectDirectory("ClassLibrary1.csproj", classLibraryContentsToolsV4);
                string projectFileV12 = ObjectModelHelpers.CreateFileInTempProjectDirectory("ClassLibrary2.csproj", classLibraryContentsToolsV12);

                SolutionFile sp1 = new SolutionFile();

                sp1.FullPath = solutionFileMultipleProjects;
                sp1.ParseSolutionFile();
                ProjectInstance[] instances1 = SolutionProjectGenerator.Generate(sp1, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

                MockLogger logger1 = new MockLogger();
                List<ILogger> loggers1 = new List<ILogger>(1);
                loggers1.Add(logger1);

                instances1[0].Build(loggers1);
                logger1.AssertLogContains(".[11.0]. .[4.0].");
                logger1.AssertLogContains(String.Format(".[{0}]. .[{0}].", ObjectModelHelpers.MSBuildDefaultToolsVersion));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", previousLegacyEnvironmentVariable);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// Test to make sure that, when we're not TV 4.0 -- which even for Dev11 solutions we are not by default -- that we
        /// do not pass VisualStudioVersion down to the child projects.  
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void SolutionDoesntPassSubToolsetToChildProjects()
        {
            try
            {
                string classLibraryContents =
                    @"
                        <Project ToolsVersion=""4.0"" DefaultTargets=""Build"" xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <Target Name='Build'>
                                <Message Text='.[$(VisualStudioVersion)].' />
                                <Message Text='.[[$(MSBuildToolsVersion)]].' />
                            </Target>
                        </Project>
                    ";

                string projectFile = ObjectModelHelpers.CreateFileInTempProjectDirectory("ClassLibrary1.csproj", classLibraryContents);

                string solutionFileContents =
                    @"
                        Microsoft Visual Studio Solution File, Format Version 12.00
                        # Visual Studio Dev11
                        Project(""{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}"") = ""ClassLibrary1"", ""ClassLibrary1.csproj"", ""{6185CC21-BE89-448A-B3C0-D1C27112E595}""
                        EndProject
                        Global
                            GlobalSection(SolutionConfigurationPlatforms) = preSolution
                                Debug|Mixed Platforms = Debug|Mixed Platforms
                                Release|Any CPU = Release|Any CPU
                            EndGlobalSection
                            GlobalSection(ProjectConfigurationPlatforms) = postSolution
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                                {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                            EndGlobalSection
                        EndGlobal
                    ";

                string solutionFile = ObjectModelHelpers.CreateFileInTempProjectDirectory("Foo.sln", solutionFileContents);

                SolutionFile sp = new SolutionFile();

                sp.FullPath = solutionFile;
                sp.ParseSolutionFile();

                ProjectInstance[] instances = SolutionProjectGenerator.Generate(sp, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);

                Assert.AreEqual(ObjectModelHelpers.MSBuildDefaultToolsVersion, instances[0].ToolsVersion);
                Assert.AreEqual("11.0", instances[0].SubToolsetVersion);
                Assert.AreEqual("11.0", instances[0].GetPropertyValue("VisualStudioVersion"));

                MockLogger logger = new MockLogger();
                List<ILogger> loggers = new List<ILogger>(1);
                loggers.Add(logger);


                instances[0].Build(loggers);
                logger.AssertLogContains(String.Format(".[{0}].", ObjectModelHelpers.MSBuildDefaultToolsVersion));
            }
            finally
            {
                ObjectModelHelpers.DeleteTempProjectDirectory();
            }
        }


        /// <summary>
        /// Verify that we throw the appropriate error if the solution declares a dependency 
        /// on a project that doesn't exist.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void SolutionWithMissingDependencies()
        {
            string solutionFileContents =
                @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 11
Project(`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) = `B`, `Project2\B.csproj`, `{881C1674-4ECA-451D-85B6-D7C59B7F16FA}`
	ProjectSection(ProjectDependencies) = postProject
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167} = {4A727FF8-65F2-401E-95AD-7C8BBFBE3167}
	EndProjectSection
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Debug|x64 = Debug|x64
		Release|Any CPU = Release|Any CPU
		Release|x64 = Release|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = preSolution
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|x64.ActiveCfg = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|x64.Build.0 = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|Any CPU.Build.0 = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|x64.ActiveCfg = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|x64.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
".Replace("`", "\"");

            SolutionFile sp = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);
            ProjectInstance[] instances = SolutionProjectGenerator.Generate(sp, null, null, new DefaultLicenseValidator(0, 0, 0, 0), null);
        }

        /// <summary>
        /// Blob should contain dependency info
        /// Here B depends on C
        /// </summary>
        [TestMethod]
        public void SolutionConfigurationWithDependencies()
        {
            string solutionFileContents =
                @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 11
Project(`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) = `A`, `Project1\A.csproj`, `{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}`
EndProject
Project(`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) = `B`, `Project2\B.csproj`, `{881C1674-4ECA-451D-85B6-D7C59B7F16FA}`
	ProjectSection(ProjectDependencies) = postProject
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167} = {4A727FF8-65F2-401E-95AD-7C8BBFBE3167}
	EndProjectSection
EndProject
Project(`{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}`) = `C`, `Project3\C.csproj`, `{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}`
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Debug|x64 = Debug|x64
		Release|Any CPU = Release|Any CPU
		Release|x64 = Release|x64
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = preSolution
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Debug|x64.ActiveCfg = Debug|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Debug|x64.Build.0 = Debug|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Release|Any CPU.Build.0 = Release|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Release|x64.ActiveCfg = Release|Any CPU
		{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}.Release|x64.Build.0 = Release|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Debug|x64.ActiveCfg = Debug|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Debug|x64.Build.0 = Debug|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Release|Any CPU.Build.0 = Release|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Release|x64.ActiveCfg = Release|Any CPU
		{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}.Release|x64.Build.0 = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|x64.ActiveCfg = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Debug|x64.Build.0 = Debug|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|Any CPU.Build.0 = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|x64.ActiveCfg = Release|Any CPU
		{881C1674-4ECA-451D-85B6-D7C59B7F16FA}.Release|x64.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
".Replace("`", "\"");

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectRootElement projectXml = ProjectRootElement.Create();

            foreach (SolutionConfigurationInSolution solutionConfiguration in solution.SolutionConfigurations)
            {
                SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration(projectXml, solution, solutionConfiguration);
            }

            Project msbuildProject = new Project(projectXml);

            // Both projects configurations should be present for solution configuration "Debug|Mixed Platforms"
            msbuildProject.SetGlobalProperty("Configuration", "Debug");
            msbuildProject.SetGlobalProperty("Platform", "Any CPU");
            msbuildProject.ReevaluateIfNecessary();

            string solutionConfigurationContents = msbuildProject.GetPropertyValue("CurrentSolutionConfigurationContents");

            // Only the specified solution configuration is represented in THE BLOB: nothing for x64 in this case
            string expected = @"<SolutionConfiguration>
  <ProjectConfiguration Project=`{786E302A-96CE-43DC-B640-D6B6CC9BF6C0}` AbsolutePath=`##temp##Project1\A.csproj` BuildProjectInSolution=`True`>Debug|AnyCPU</ProjectConfiguration>
  <ProjectConfiguration Project=`{881C1674-4ECA-451D-85B6-D7C59B7F16FA}` AbsolutePath=`##temp##Project2\B.csproj` BuildProjectInSolution=`True`>Debug|AnyCPU<ProjectDependency Project=`{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}` /></ProjectConfiguration>
  <ProjectConfiguration Project=`{4A727FF8-65F2-401E-95AD-7C8BBFBE3167}` AbsolutePath=`##temp##Project3\C.csproj` BuildProjectInSolution=`True`>Debug|AnyCPU</ProjectConfiguration>
</SolutionConfiguration>".Replace("`", "\"").Replace("##temp##", Path.GetTempPath());

            Helpers.VerifyAssertLineByLine(expected, solutionConfigurationContents);
        }

        /// <summary>
        /// Test the SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration method
        /// </summary>
        [TestMethod]
        public void TestAddPropertyGroupForSolutionConfiguration()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Project('{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}') = 'MainApp', 'MainApp\MainApp.vcxproj', '{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.ActiveCfg = VCConfig1|Win32
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.Build.0 = VCConfig1|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectRootElement projectXml = ProjectRootElement.Create();

            foreach (SolutionConfigurationInSolution solutionConfiguration in solution.SolutionConfigurations)
            {
                SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration(projectXml, solution, solutionConfiguration);
            }

            Project msbuildProject = new Project(projectXml);

            // Both projects configurations should be present for solution configuration "Debug|Mixed Platforms"
            msbuildProject.SetGlobalProperty("Configuration", "Debug");
            msbuildProject.SetGlobalProperty("Platform", "Mixed Platforms");
            msbuildProject.ReevaluateIfNecessary();

            string solutionConfigurationContents = msbuildProject.GetPropertyValue("CurrentSolutionConfigurationContents");
            string tempProjectPath = Path.Combine(Path.GetTempPath(), "ClassLibrary1\\ClassLibrary1.csproj");

            Assert.IsTrue(solutionConfigurationContents.Contains("{6185CC21-BE89-448A-B3C0-D1C27112E595}"));
            tempProjectPath = Path.GetFullPath(tempProjectPath);
            Assert.IsTrue(solutionConfigurationContents.IndexOf(tempProjectPath, StringComparison.OrdinalIgnoreCase) > 0);
            Assert.IsTrue(solutionConfigurationContents.Contains("CSConfig1|AnyCPU"));

            tempProjectPath = Path.Combine(Path.GetTempPath(), "MainApp\\MainApp.vcxproj");
            tempProjectPath = Path.GetFullPath(tempProjectPath);
            Assert.IsTrue(solutionConfigurationContents.Contains("{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}"));
            Assert.IsTrue(solutionConfigurationContents.IndexOf(tempProjectPath, StringComparison.OrdinalIgnoreCase) > 0);
            Assert.IsTrue(solutionConfigurationContents.Contains("VCConfig1|Win32"));

            // Only the C# project should be present for solution configuration "Release|Any CPU", since the VC project
            // is missing
            msbuildProject.SetGlobalProperty("Configuration", "Release");
            msbuildProject.SetGlobalProperty("Platform", "Any CPU");
            msbuildProject.ReevaluateIfNecessary();

            solutionConfigurationContents = msbuildProject.GetPropertyValue("CurrentSolutionConfigurationContents");

            Assert.IsTrue(solutionConfigurationContents.Contains("{6185CC21-BE89-448A-B3C0-D1C27112E595}"));
            Assert.IsTrue(solutionConfigurationContents.Contains("CSConfig2|AnyCPU"));

            Assert.IsFalse(solutionConfigurationContents.Contains("{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}"));
        }

        /// <summary>
        /// Make sure that BuildProjectInSolution is set to true of the Build.0 entry is in the solution configuration.
        /// </summary>
        [TestMethod]
        public void TestAddPropertyGroupForSolutionConfigurationBuildProjectInSolutionSet()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectRootElement projectXml = ProjectRootElement.Create();

            foreach (SolutionConfigurationInSolution solutionConfiguration in solution.SolutionConfigurations)
            {
                SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration(projectXml, solution, solutionConfiguration);
            }

            Project msbuildProject = new Project(projectXml);

            // Both projects configurations should be present for solution configuration "Debug|Mixed Platforms"
            msbuildProject.SetGlobalProperty("Configuration", "Debug");
            msbuildProject.SetGlobalProperty("Platform", "Mixed Platforms");
            msbuildProject.ReevaluateIfNecessary();

            string solutionConfigurationContents = msbuildProject.GetPropertyValue("CurrentSolutionConfigurationContents");
            Assert.IsTrue(solutionConfigurationContents.Contains(@"BuildProjectInSolution=""" + bool.TrueString + @""""));
        }

        /// <summary>
        /// Make sure that BuildProjectInSolution is set to false of the Build.0 entry is in the solution configuration.
        /// </summary>
        [TestMethod]
        public void TestAddPropertyGroupForSolutionConfigurationBuildProjectInSolutionNotSet()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                     GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectRootElement projectXml = ProjectRootElement.Create();

            foreach (SolutionConfigurationInSolution solutionConfiguration in solution.SolutionConfigurations)
            {
                SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration(projectXml, solution, solutionConfiguration);
            }

            Project msbuildProject = new Project(projectXml);

            // Both projects configurations should be present for solution configuration "Debug|Mixed Platforms"
            msbuildProject.SetGlobalProperty("Configuration", "Debug");
            msbuildProject.SetGlobalProperty("Platform", "Mixed Platforms");
            msbuildProject.ReevaluateIfNecessary();

            string solutionConfigurationContents = msbuildProject.GetPropertyValue("CurrentSolutionConfigurationContents");
            Assert.IsTrue(solutionConfigurationContents.Contains(@"BuildProjectInSolution=""" + bool.FalseString + @""""));
        }

        /// <summary>
        /// In this bug, SkipNonexistentProjects was always set to 'Build'. It should be 'Build' for metaprojects and 'True' for everything else.
        /// The repro below has one of each case. WebProjects can't build so they are set as SkipNonexistentProjects='Build'
        /// </summary>
        [TestMethod]
        public void Regress751742_SkipNonexistentProjects()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                // ".NET Framework 2.0 is required to be installed for this test, but it is not installed."
                return;
            }

            var solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'MainApp', 'MainApp\MainApp.webproj', '{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.ActiveCfg = VCConfig1|Win32
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.Build.0 = VCConfig1|Win32
                    EndGlobalSection
                EndGlobal
                ";

            // We're not passing in a /tv:xx switch, so the solution project will have tools version 2.0
            var solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);
            var DefaultLicenseValidator = new DefaultLicenseValidator(0, 0, 0, 0);

            var instance = SolutionProjectGenerator.Generate(solution, null, ObjectModelHelpers.MSBuildDefaultToolsVersion, DefaultLicenseValidator, null)[0];

            foreach (ITaskItem item in instance.Items)
            {
                string skipNonexistentProjects = item.GetMetadata("SkipNonexistentProjects");
                if (item.ItemSpec.EndsWith("ClassLibrary1.csproj"))
                {
                    Assert.AreEqual("False", skipNonexistentProjects);
                }
                else if (item.ItemSpec.EndsWith("MainApp.metaproj"))
                {
                    Assert.AreEqual("Build", skipNonexistentProjects);
                }
                else if (item.ItemSpec == "Debug|Mixed Platforms")
                {
                    Assert.AreEqual("Debug", item.GetMetadata("Configuration"));
                    Assert.AreEqual("Mixed Platforms", item.GetMetadata("Platform"));
                    Assert.IsTrue(item.GetMetadata("Content").Contains("<SolutionConfiguration>"));
                }
                else if (item.ItemSpec == "Release|Any CPU")
                {
                    Assert.AreEqual("Release", item.GetMetadata("Configuration"));
                    Assert.AreEqual("Any CPU", item.GetMetadata("Platform"));
                    Assert.IsTrue(item.GetMetadata("Content").Contains("<SolutionConfiguration>"));
                }
                else
                {
                    Assert.Fail("Unexpected project seen:" + item.ItemSpec);
                }
            }
        }



        /// <summary>
        /// Test that the in memory project created from a solution file exposes an MSBuild property which,
        /// if set when building a solution, will be specified as the ToolsVersion on the MSBuild task when
        /// building the projects contained within the solution.
        /// </summary>
        [TestMethod]
        public void ToolsVersionOverrideShouldBeSpecifiedOnMSBuildTaskInvocations()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Project('{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}') = 'MainApp', 'MainApp\MainApp.vcxproj', '{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.ActiveCfg = VCConfig1|Win32
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.Build.0 = VCConfig1|Win32
                    EndGlobalSection
                EndGlobal
                ";

            // We're not passing in a /tv:xx switch, so the solution project will have tools version 2.0
            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);
            DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(0, 0, 0, 0);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, ObjectModelHelpers.MSBuildDefaultToolsVersion, DefaultLicenseValidator, null);

            int i = 0;
            foreach (ProjectInstance instance in instances)
            {
                if (i == 0)
                {
                    continue;
                }

                foreach (ProjectTargetInstance target in instance.Targets.Values)
                {
                    foreach (ProjectTaskInstance childNode in target.Tasks)
                    {
                        if (0 == String.Compare(childNode.Name, "MSBuild", StringComparison.OrdinalIgnoreCase))
                        {
                            string projectsParameter = childNode.GetParameter("Projects");
                            if (projectsParameter != "@(ProjectReference)")
                            {
                                // we found an MSBuild task invocation, now let's verify that it has the correct
                                // ToolsVersion parameter set
                                string toolsVersionParameter = childNode.GetParameter("ToolsVersion");

                                Assert.AreEqual(0, String.Compare(toolsVersionParameter, instances[0].GetPropertyValue("ProjectToolsVersion"), StringComparison.OrdinalIgnoreCase));
                            }
                        }
                    }
                }

                i++;
            }
        }

        /// <summary>
        /// Make sure that whatever the solution ToolsVersion is, it gets mapped to all its metaprojs, too. 
        /// </summary>
        [TestMethod]
        public void SolutionWithDependenciesHasCorrectToolsVersionInMetaprojs()
        {
            string solutionFileContents =
                @"
Microsoft Visual Studio Solution File, Format Version 12.00
# Visual Studio 12
Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ConsoleApplication2', 'ConsoleApplication2\ConsoleApplication2.csproj', '{5B97A3C7-3DEE-47A4-870F-5CB6384FE6A4}'
	ProjectSection(ProjectDependencies) = postProject
		{E0D295A1-CAFA-4E68-9929-468657DAAC6C} = {E0D295A1-CAFA-4E68-9929-468657DAAC6C}
	EndProjectSection
EndProject
Project('{F184B08F-C81C-45F6-A57F-5ABD9991F28F}') = 'ConsoleApplication1', 'ConsoleApplication1\ConsoleApplication1.vbproj', '{E0D295A1-CAFA-4E68-9929-468657DAAC6C}'
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
		Release|Any CPU = Release|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{5B97A3C7-3DEE-47A4-870F-5CB6384FE6A4}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{5B97A3C7-3DEE-47A4-870F-5CB6384FE6A4}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{5B97A3C7-3DEE-47A4-870F-5CB6384FE6A4}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{5B97A3C7-3DEE-47A4-870F-5CB6384FE6A4}.Release|Any CPU.Build.0 = Release|Any CPU
		{E0D295A1-CAFA-4E68-9929-468657DAAC6C}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{E0D295A1-CAFA-4E68-9929-468657DAAC6C}.Debug|Any CPU.Build.0 = Debug|Any CPU
		{E0D295A1-CAFA-4E68-9929-468657DAAC6C}.Release|Any CPU.ActiveCfg = Release|Any CPU
		{E0D295A1-CAFA-4E68-9929-468657DAAC6C}.Release|Any CPU.Build.0 = Release|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
                ";

            // We're not passing in a /tv:xx switch, so the solution project will have tools version 2.0
            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);
            DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(0, 0, 0, 0);

            string[] solutionToolsVersions = { "4.0", ObjectModelHelpers.MSBuildDefaultToolsVersion };

            foreach (string solutionToolsVersion in solutionToolsVersions)
            {
                ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, solutionToolsVersion, DefaultLicenseValidator, null);

                Assert.AreEqual(2, instances.Length);

                // Solution metaproj 
                Assert.AreEqual(solutionToolsVersion, instances[0].ToolsVersion);

                ICollection<ProjectItemInstance> projectReferences = instances[0].GetItems("ProjectReference");

                foreach (ProjectItemInstance projectReference in projectReferences)
                {
                    // If this is the reference to the metaproj, its ToolsVersion metadata needs to match 
                    // the solution ToolsVersion -- that's how the build knows which ToolsVersion to use. 
                    if (projectReference.EvaluatedInclude.EndsWith(".metaproj", StringComparison.OrdinalIgnoreCase))
                    {
                        Assert.AreEqual(solutionToolsVersion, projectReference.GetMetadataValue("ToolsVersion"));
                    }
                }

                // Project metaproj for project with dependencies 
                Assert.AreEqual(solutionToolsVersion, instances[1].ToolsVersion);
            }
        }

        /// <summary>
        /// Test the SolutionProjectGenerator.Generate method has its toolset redirected correctly.
        /// </summary>
        [TestMethod]
        public void ToolsVersionOverrideCausesToolsetRedirect()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'ClassLibrary1', 'ClassLibrary1\ClassLibrary1.csproj', '{6185CC21-BE89-448A-B3C0-D1C27112E595}'
                EndProject
                Project('{8BC9CEB8-8B4A-11D0-8D11-00A0C91BC942}') = 'MainApp', 'MainApp\MainApp.vcxproj', '{A6F99D27-47B9-4EA4-BFC9-25157CBDC281}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Release|Any CPU = Release|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.ActiveCfg = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Debug|Mixed Platforms.Build.0 = CSConfig1|Any CPU
                        {6185CC21-BE89-448A-B3C0-D1C27112E595}.Release|Any CPU.ActiveCfg = CSConfig2|Any CPU
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.ActiveCfg = VCConfig1|Win32
                        {A6F99D27-47B9-4EA4-BFC9-25157CBDC281}.Debug|Mixed Platforms.Build.0 = VCConfig1|Win32
                    EndGlobalSection
                EndGlobal
                ";

            ProjectInstance[] instances = null;
            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);
            DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(0, 0, 0, 0);
            bool caughtException = false;

            try
            {
                // SolutionProjectGenerator.Generate() is used at build-time, and creates evaluation- and 
                // execution-model projects; as such it will throw if fed an explicitly invalid toolsversion
                instances = SolutionProjectGenerator.Generate(solution, null, "invalid", DefaultLicenseValidator, null);
            }
            catch (InvalidProjectFileException)
            {
                caughtException = true;
            }

            Assert.IsTrue(caughtException, "Passing an invalid ToolsVersion should have caused an InvalidProjectFileException to be thrown.");
        }

        /// <summary>
        /// Test the SolutionProjectGenerator.AddPropertyGroupForSolutionConfiguration method
        /// </summary>
        [TestMethod]
        public void TestDisambiguateProjectTargetName()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}') = 'Build', 'Build\Build.csproj', '{21397922-C38F-4A0E-B950-77B3FBD51881}'
                EndProject
                Global
                        GlobalSection(SolutionConfigurationPlatforms) = preSolution
                                Debug|Any CPU = Debug|Any CPU
                                Release|Any CPU = Release|Any CPU
                        EndGlobalSection
                        GlobalSection(ProjectConfigurationPlatforms) = postSolution
                                {21397922-C38F-4A0E-B950-77B3FBD51881}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
                                {21397922-C38F-4A0E-B950-77B3FBD51881}.Debug|Any CPU.Build.0 = Debug|Any CPU
                                {21397922-C38F-4A0E-B950-77B3FBD51881}.Release|Any CPU.ActiveCfg = Release|Any CPU
                                {21397922-C38F-4A0E-B950-77B3FBD51881}.Release|Any CPU.Build.0 = Release|Any CPU
                        EndGlobalSection
                        GlobalSection(SolutionProperties) = preSolution
                                HideSolutionNode = FALSE
                        EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);

            Assert.AreEqual(1, instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Build", StringComparison.OrdinalIgnoreCase) == 0).Count());
            Assert.AreEqual(1, instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Clean", StringComparison.OrdinalIgnoreCase) == 0).Count());
            Assert.AreEqual(1, instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Rebuild", StringComparison.OrdinalIgnoreCase) == 0).Count());
            Assert.AreEqual(1, instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Publish", StringComparison.OrdinalIgnoreCase) == 0).Count());

            ProjectTargetInstance buildTarget = instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Build", StringComparison.OrdinalIgnoreCase) == 0).First().Value;
            ProjectTargetInstance cleanTarget = instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Clean", StringComparison.OrdinalIgnoreCase) == 0).First().Value;
            ProjectTargetInstance rebuildTarget = instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Rebuild", StringComparison.OrdinalIgnoreCase) == 0).First().Value;
            ProjectTargetInstance publishTarget = instances[0].Targets.Where(target => String.Compare(target.Value.Name, "Publish", StringComparison.OrdinalIgnoreCase) == 0).First().Value;

            // Check that the appropriate target is being passed to the child projects
            Assert.AreEqual(null, buildTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Targets"));

            Assert.AreEqual("Clean", cleanTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Targets"));

            Assert.AreEqual("Rebuild", rebuildTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Targets"));

            Assert.AreEqual("Publish", publishTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Targets"));

            // Check that the child projects in question are the members of the "ProjectReference" item group
            Assert.AreEqual("@(ProjectReference)", buildTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Projects"));

            Assert.AreEqual("@(ProjectReference->Reverse())", cleanTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Projects"));

            Assert.AreEqual("@(ProjectReference)", rebuildTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Projects"));

            Assert.AreEqual("@(ProjectReference)", publishTarget.Tasks.Where
                (
                task => String.Compare(task.Name, "MSBuild", StringComparison.OrdinalIgnoreCase) == 0
                ).First().GetParameter("Projects"));

            // We should have only the four standard targets plus the two validation targets (ValidateSolutionConfiguration and ValidateToolsVersions).
        }

        /// <summary>
        /// Tests the algorithm for choosing default configuration/platform values for solutions
        /// </summary>
        [TestMethod]
        public void TestConfigurationPlatformDefaults1()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Debug|Win32 = Debug|Win32
                        Release|Any CPU = Release|Any CPU
                        Release|Mixed Platforms = Release|Mixed Platforms
                        Release|Win32 = Release|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            // These used to exist on the engine, but now need to be passed in explicitly
            IDictionary<string, string> globalProperties = new Dictionary<string, string>();

            globalProperties.Add(new KeyValuePair<string, string>("Configuration", "Debug"));
            globalProperties.Add(new KeyValuePair<string, string>("Platform", "Mixed Platforms"));

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);

            // Default for Configuration is "Debug", if present
            Assert.AreEqual("Debug", instances[0].GetPropertyValue("Configuration"));

            // Default for Platform is "Mixed Platforms", if present
            Assert.AreEqual("Mixed Platforms", instances[0].GetPropertyValue("Platform"));
        }

        /// <summary>
        /// Tests the algorithm for choosing default configuration/platform values for solutions
        /// </summary>
        [TestMethod]
        public void TestConfigurationPlatformDefaults2()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Any CPU = Release|Any CPU
                        Release|Win32 = Release|Win32
                        Other|Any CPU = Other|Any CPU
                        Other|Win32 = Other|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);

            // If "Debug" is not present, just pick the first configuration name
            Assert.AreEqual("Release", instances[0].GetPropertyValue("Configuration"));

            // if "Mixed Platforms" is not present, just pick the first platform name
            Assert.AreEqual("Any CPU", instances[0].GetPropertyValue("Platform"));
        }

        /// <summary>
        /// Tests the algorithm for choosing default Venus configuration values for solutions
        /// </summary>
        [TestMethod]
        public void TestVenusConfigurationDefaults()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                // ".NET Framework 2.0 is required to be installed for this test, but it is not installed."
                return;
            }

            Dictionary<string, string> globalProperties = new Dictionary<string, string>();
            globalProperties["Configuration"] = "Debug";
            ProjectInstance msbuildProject = CreateVenusSolutionProject(globalProperties);

            // ASP.NET configuration should match the selected solution configuration
            Assert.AreEqual("Debug", msbuildProject.GetPropertyValue("AspNetConfiguration"));

            globalProperties["Configuration"] = "Release";
            msbuildProject = CreateVenusSolutionProject(globalProperties);
            Assert.AreEqual("Release", msbuildProject.GetPropertyValue("AspNetConfiguration"));

            // Check that the two standard Asp.net configurations are represented on the targets
            Assert.IsTrue(msbuildProject.Targets["Build"].Condition.Contains("'$(Configuration)' == 'Release'"));
            Assert.IsTrue(msbuildProject.Targets["Build"].Condition.Contains("'$(Configuration)' == 'Debug'"));
        }

        /// <summary>
        /// Tests that the correct value for TargetFrameworkVersion gets set when creating Venus solutions
        /// </summary>
        [TestMethod]
        public void VenusSolutionDefaultTargetFrameworkVersion()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                // ".NET Framework 2.0 is required to be installed for this test, but it is not installed."
                return;
            }

            // v4.0 by default
            ProjectInstance msbuildProject = CreateVenusSolutionProject();
            Assert.AreEqual("v4.0", msbuildProject.GetPropertyValue("TargetFrameworkVersion"));

            if (FrameworkLocationHelper.PathToDotNetFrameworkV35 == null)
            {
                // ".NET Framework 3.5 is required to be installed for this test, but it is not installed."
                return;
            }

            // v3.5 if MSBuildToolsVersion is 3.5
            msbuildProject = CreateVenusSolutionProject("3.5");
            Assert.AreEqual("v3.5", msbuildProject.GetPropertyValue("TargetFrameworkVersion"));

            // v2.0 if MSBuildToolsVersion is 2.0
            msbuildProject = CreateVenusSolutionProject("2.0");
            Assert.AreEqual("v2.0", msbuildProject.GetPropertyValue("TargetFrameworkVersion"));

            // may be user defined 
            IDictionary<string, string> globalProperties = new Dictionary<string, string>();
            globalProperties.Add("TargetFrameworkVersion", "userdefined");
            msbuildProject = CreateVenusSolutionProject(globalProperties);
            Assert.AreEqual("userdefined", msbuildProject.GetPropertyValue("TargetFrameworkVersion"));
        }

        /// <summary>
        /// Tests the algorithm for choosing target framework paths for ResolveAssemblyReferences for Venus
        /// </summary>
        [TestMethod]
        public void TestTargetFrameworkPaths0()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkSdkV20 != null)
            {
                IDictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties.Add("TargetFrameworkVersion", "v2.0");

                ProjectInstance msbuildProject = CreateVenusSolutionProject("2.0");

                // ToolsVersion is 2.0, TargetFrameworkVersion is v2.0 --> one item pointing to v2.0
                Assert.AreEqual("2.0", msbuildProject.ToolsVersion);

                bool success = msbuildProject.Build("GetFrameworkPathAndRedistList", null);
                Assert.AreEqual(true, success);
                AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV20);
                AssertProjectItemNameCount(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", 1);
            }
        }

        /// <summary>
        /// Tests the algorithm for choosing target framework paths for ResolveAssemblyReferences for Venus
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void TestTargetFrameworkPaths1()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                // ".NET Framework 2.0 is required to be installed for this test, but it is not installed."
                return;
            }

            ProjectInstance msbuildProject = CreateVenusSolutionProject();

            // ToolsVersion is 4.0, TargetFrameworkVersion is v2.0 --> one item pointing to v2.0
            msbuildProject.SetProperty("TargetFrameworkVersion", "v2.0");
            MockLogger logger = new MockLogger();
            bool success = msbuildProject.Build("GetFrameworkPathAndRedistList", new ILogger[] { logger });
            Assert.AreEqual(true, success);

            AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV20);
            AssertProjectItemNameCount(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", 1);
        }

        /// <summary>
        /// Tests the algorithm for choosing target framework paths for ResolveAssemblyReferences for Venus
        /// </summary>
        [TestMethod]
        public void TestTargetFrameworkPaths2()
        {
            if (FrameworkLocationHelper.PathToDotNetFrameworkV20 == null)
            {
                // ".NET Framework 2.0 is required to be installed for this test, but it is not installed."
                return;
            }

            ProjectInstance msbuildProject = CreateVenusSolutionProject();

            // ToolsVersion is 4.0, TargetFrameworkVersion is v4.0 --> items for v2.0 and v4.0
            msbuildProject.SetProperty("TargetFrameworkVersion", "v4.0");
            // ProjectInstance projectToBuild = msbuildProject.CreateProjectInstance();
            bool success = msbuildProject.Build("GetFrameworkPathAndRedistList", null);
            Assert.AreEqual(true, success);

            int expectedCount = 0;

            // 2.0 must be installed for us to have come this far
            AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV20);
            expectedCount++;

            if (FrameworkLocationHelper.PathToDotNetFrameworkV30 != null)
            {
                AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV30);
                expectedCount++;
            }

            if (FrameworkLocationHelper.PathToDotNetFrameworkV35 != null)
            {
                AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV35);
                expectedCount++;
            }

            if (FrameworkLocationHelper.PathToDotNetFrameworkV40 != null)
            {
                AssertProjectContainsItem(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", FrameworkLocationHelper.PathToDotNetFrameworkV40);
                expectedCount++;
            }

            AssertProjectItemNameCount(msbuildProject, "_CombinedTargetFrameworkDirectoriesItem", expectedCount);
        }

        /// <summary>
        /// Test the PredictActiveSolutionConfigurationName method
        /// </summary>
        [TestMethod]
        public void TestPredictSolutionConfigurationName()
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Release|Mixed Platforms = Release|Mixed Platforms
                        Release|Win32 = Release|Win32
                        Debug|Mixed Platforms = Debug|Mixed Platforms
                        Debug|Win32 = Debug|Win32
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            IDictionary<string, string> globalProperties = new Dictionary<string, string>();

            Assert.AreEqual("Debug|Mixed Platforms", SolutionProjectGenerator.PredictActiveSolutionConfigurationName(solution, globalProperties));

            globalProperties.Add("Configuration", "Release");
            Assert.AreEqual("Release|Mixed Platforms", SolutionProjectGenerator.PredictActiveSolutionConfigurationName(solution, globalProperties));

            globalProperties.Add("Platform", "Win32");
            Assert.AreEqual("Release|Win32", SolutionProjectGenerator.PredictActiveSolutionConfigurationName(solution, globalProperties));

            globalProperties["Configuration"] = "Nonexistent";
            Assert.AreEqual(null, SolutionProjectGenerator.PredictActiveSolutionConfigurationName(solution, globalProperties));
        }


        /// <summary>
        /// Verifies that the SolutionProjectGenerator will correctly escape project file paths
        /// </summary>
        [TestMethod]
        public void SolutionGeneratorEscapingProjectFilePaths()
        {
            string oldValueForMSBuildEmitSolution = Environment.GetEnvironmentVariable("MSBuildEmitSolution");

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{F184B08F-C81C-45F6-A57F-5ABD9991F28F}') = 'ConsoleApplication1', '%abtest\ConsoleApplication1.vbproj', '{AB3413A6-D689-486D-B7F0-A095371B3F13}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|AnyCPU = Debug|AnyCPU
                        Release|AnyCPU = Release|AnyCPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Debug|AnyCPU.ActiveCfg = Debug|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Debug|AnyCPU.Build.0 = Debug|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Release|AnyCPU.ActiveCfg = Release|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Release|AnyCPU.Build.0 = Release|AnyCPU
                    EndGlobalSection
                    GlobalSection(SolutionProperties) = preSolution
                        HideSolutionNode = FALSE
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = null;

            solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            // Creating a ProjectRootElement shouldn't affect the ProjectCollection at all
            Assert.AreEqual(0, ProjectCollection.GlobalProjectCollection.LoadedProjects.Count());

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);

            Assert.AreEqual(0, ProjectCollection.GlobalProjectCollection.LoadedProjects.Count());

            // Ensure that the value has been correctly stored in the ProjectReference item list
            // Since there is only one project in the solution, there will be only one project reference
            Assert.IsTrue(instances[0].GetItems("ProjectReference").ElementAt(0).EvaluatedInclude.Contains("%abtest"));
        }

        /// <summary>
        /// Verifies that the SolutionProjectGenerator will emit a solution file.
        /// </summary>
        [TestMethod]
        public void SolutionGeneratorCanEmitSolutions()
        {
            string oldValueForMSBuildEmitSolution = Environment.GetEnvironmentVariable("MSBuildEmitSolution");

            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{F184B08F-C81C-45F6-A57F-5ABD9991F28F}') = 'ConsoleApplication1', 'ConsoleApplication1\ConsoleApplication1.vbproj', '{AB3413A6-D689-486D-B7F0-A095371B3F13}'
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|AnyCPU = Debug|AnyCPU
                        Release|AnyCPU = Release|AnyCPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Debug|AnyCPU.ActiveCfg = Debug|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Debug|AnyCPU.Build.0 = Debug|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Release|AnyCPU.ActiveCfg = Release|AnyCPU
                        {AB3413A6-D689-486D-B7F0-A095371B3F13}.Release|AnyCPU.Build.0 = Release|AnyCPU
                    EndGlobalSection
                    GlobalSection(SolutionProperties) = preSolution
                        HideSolutionNode = FALSE
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = null;

            try
            {
                Environment.SetEnvironmentVariable("MSBuildEmitSolution", "1");

                solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

                // Creating a ProjectRootElement shouldn't affect the ProjectCollection at all
                Assert.AreEqual(0, ProjectCollection.GlobalProjectCollection.LoadedProjects.Count());

                ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);

                // Instantiating the
                Assert.AreEqual(0, ProjectCollection.GlobalProjectCollection.LoadedProjects.Count());
            }
            finally
            {
                // reset the environment variable first so that it doesn't get ignored by the assert.
                Environment.SetEnvironmentVariable("MSBuildEmitSolution", oldValueForMSBuildEmitSolution);

                // Clean up.  Delete temp files and reset environment variables.
                if (solution != null)
                {
                    Assert.IsTrue(File.Exists(solution.FullPath + ".metaproj"), "Solution parser should have written in-memory project to disk");
                    File.Delete(solution.FullPath + ".metaproj");
                }
                else
                {
                    Assert.Fail("Something went really wrong!  The SolutionFile wasn't even created!");
                }
            }
        }

        /// <summary>
        /// Make sure that we output a warning and don't build anything when we're given an invalid
        /// solution configuration and SkipInvalidConfigurations is set to true.
        /// </summary>
        [TestMethod]
        public void TestSkipInvalidConfigurationsCase()
        {
            string tmpFileName = FileUtilities.GetTemporaryFile();
            File.Delete(tmpFileName);
            string projectFilePath = tmpFileName + ".sln";

            string solutionContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 11.00
                # Visual Studio 2005
                Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'C:\solutions\WebSite2\', '..\..\solutions\WebSite2\', '{F90528C4-6989-4D33-AFE8-F53173597CC2}'
                    ProjectSection(WebsiteProperties) = preProject
                        Debug.AspNetCompiler.VirtualPath = '/WebSite2'
                        Debug.AspNetCompiler.PhysicalPath = '..\..\solutions\WebSite2\'
                        Debug.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite2\'
                        Debug.AspNetCompiler.Updateable = 'true'
                        Debug.AspNetCompiler.ForceOverwrite = 'true'
                        Debug.AspNetCompiler.FixedNames = 'true'
                        Debug.AspNetCompiler.Debug = 'True'
                        Release.AspNetCompiler.VirtualPath = '/WebSite2'
                        Release.AspNetCompiler.PhysicalPath = '..\..\solutions\WebSite2\'
                        Release.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite2\'
                        Release.AspNetCompiler.Updateable = 'true'
                        Release.AspNetCompiler.ForceOverwrite = 'true'
                        Release.AspNetCompiler.FixedNames = 'true'
                        Release.AspNetCompiler.Debug = 'False'
                        VWDPort = '2776'
                        DefaultWebSiteLanguage = 'Visual C#'
                    EndProjectSection
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {F90528C4-6989-4D33-AFE8-F53173597CC2}.Debug|Any CPU.ActiveCfg = Debug|.NET
                        {F90528C4-6989-4D33-AFE8-F53173597CC2}.Debug|Any CPU.Build.0 = Debug|.NET
                    EndGlobalSection
                EndGlobal";

            try
            {
                MockLogger logger = new MockLogger();

                Dictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties["Configuration"] = "Nonexistent";
                globalProperties["SkipInvalidConfigurations"] = "true";

                SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionContents.Replace('\'', '"'));
                ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, globalProperties, null, DefaultLicenseValidator.Invalid, null);
                ProjectInstance msbuildProject = instances[0];

                // Build should complete successfully even with an invalid solution config if SkipInvalidConfigurations is true
                Assert.AreEqual(true, msbuildProject.Build(new ILogger[] { logger }));

                // We should get the invalid solution configuration warning
                Assert.AreEqual(1, logger.Warnings.Count);
                BuildWarningEventArgs warning = logger.Warnings[0];

                // Don't look at warning.Code here -- it may be null if PseudoLoc has messed
                // with our resource strings. The code will still be in the log -- it just wouldn't get
                // pulled out into the code field.
                logger.AssertLogContains("MSB4126");

                // No errors expected
                Assert.AreEqual(0, logger.Errors.Count);
            }
            finally
            {
                File.Delete(projectFilePath);
            }
        }

        /// <summary>
        /// When we have a bad framework moniker we expect the build to fail.
        /// </summary>
        [TestMethod]
        public void BadFrameworkMonkierExpectBuildToFail()
        {
            string tmpFileName = FileUtilities.GetTemporaryFile();
            File.Delete(tmpFileName);
            string projectFilePath = tmpFileName + ".sln";

            string solutionFileContents =
                            @"Microsoft Visual Studio Solution File, Format Version 11.00
# Visual Studio 2010
Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'WebSite1', '..\WebSite1\', '{6B8F98F2-C976-4029-9321-5CCD73A174DA}'
	ProjectSection(WebsiteProperties) = preProject
		TargetFrameworkMoniker = 'SuperCoolReallyAwesomeFramework,Version=v1.0'
		Debug.AspNetCompiler.VirtualPath = '/WebSite1'
		Debug.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Debug.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Debug.AspNetCompiler.Updateable = 'true'
		Debug.AspNetCompiler.ForceOverwrite = 'true'
		Debug.AspNetCompiler.FixedNames = 'false'
		Debug.AspNetCompiler.Debug = 'True'
		Release.AspNetCompiler.VirtualPath = '/WebSite1'
		Release.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Release.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Release.AspNetCompiler.Updateable = 'true'
		Release.AspNetCompiler.ForceOverwrite = 'true'
		Release.AspNetCompiler.FixedNames = 'false'
		Release.AspNetCompiler.Debug = 'False'
		VWDPort = '45602'
		DefaultWebSiteLanguage = 'Visual Basic'
	EndProjectSection
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
                ";

            BuildManager buildManager = null;

            try
            {
                // Since we're creating our own BuildManager, we need to make sure that the default 
                // one has properly relinquished the inproc node
                NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
                if (nodeProviderInProc != null)
                {
                    nodeProviderInProc.Dispose();
                }

                File.WriteAllText(projectFilePath, solutionFileContents.Replace('\'', '"'));
                MockLogger logger = new MockLogger();

                BuildParameters parameters = new BuildParameters();
                parameters.Loggers = new ILogger[] { logger };
                parameters.EnableNodeReuse = false;
                parameters.ShutdownInProcNodeOnBuildFinish = true;
                buildManager = new BuildManager();


                Dictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties["Configuration"] = "Release";

                BuildRequestData request = new BuildRequestData(projectFilePath, globalProperties, ObjectModelHelpers.MSBuildDefaultToolsVersion, new string[0], null);
                BuildResult result = buildManager.Build(parameters, request);
                Assert.AreEqual(BuildResultCode.Failure, result.OverallResult);
                // Build should complete successfully even with an invalid solution config if SkipInvalidConfigurations is true
                logger.AssertLogContains("MSB4203");
            }
            finally
            {
                File.Delete(projectFilePath);

                if (buildManager != null)
                {
                    NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)buildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
                    nodeProviderInProc.Dispose();
                }
            }
        }

        /// <summary>
        /// When we have a bad framework moniker we expect the build to fail. In this case we are passing a poorly formatted framework moniker.
        /// This will test the exception path where the framework name is invalid rather than just not .netFramework
        /// </summary>
        [TestMethod]
        public void BadFrameworkMonkierExpectBuildToFail2()
        {
            string tmpFileName = FileUtilities.GetTemporaryFile();
            File.Delete(tmpFileName);
            string projectFilePath = tmpFileName + ".sln";

            string solutionFileContents =
                            @"Microsoft Visual Studio Solution File, Format Version 11.00
# Visual Studio 2010
Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'WebSite1', '..\WebSite1\', '{6B8F98F2-C976-4029-9321-5CCD73A174DA}'
	ProjectSection(WebsiteProperties) = preProject
		TargetFrameworkMoniker = 'Oscar the grouch'
		Debug.AspNetCompiler.VirtualPath = '/WebSite1'
		Debug.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Debug.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Debug.AspNetCompiler.Updateable = 'true'
		Debug.AspNetCompiler.ForceOverwrite = 'true'
		Debug.AspNetCompiler.FixedNames = 'false'
		Debug.AspNetCompiler.Debug = 'True'
		Release.AspNetCompiler.VirtualPath = '/WebSite1'
		Release.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Release.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Release.AspNetCompiler.Updateable = 'true'
		Release.AspNetCompiler.ForceOverwrite = 'true'
		Release.AspNetCompiler.FixedNames = 'false'
		Release.AspNetCompiler.Debug = 'False'
		VWDPort = '45602'
		DefaultWebSiteLanguage = 'Visual Basic'
	EndProjectSection
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
                ";

            BuildManager buildManager = null;

            try
            {
                // Since we're creating our own BuildManager, we need to make sure that the default 
                // one has properly relinquished the inproc node
                NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
                if (nodeProviderInProc != null)
                {
                    nodeProviderInProc.Dispose();
                }

                File.WriteAllText(projectFilePath, solutionFileContents.Replace('\'', '"'));
                MockLogger logger = new MockLogger();

                BuildParameters parameters = new BuildParameters();
                parameters.Loggers = new ILogger[] { logger };
                parameters.EnableNodeReuse = false;
                parameters.ShutdownInProcNodeOnBuildFinish = true;
                buildManager = new BuildManager();


                Dictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties["Configuration"] = "Release";

                BuildRequestData request = new BuildRequestData(projectFilePath, globalProperties, ObjectModelHelpers.MSBuildDefaultToolsVersion, new string[0], null);
                BuildResult result = buildManager.Build(parameters, request);
                Assert.AreEqual(BuildResultCode.Failure, result.OverallResult);
                // Build should complete successfully even with an invalid solution config if SkipInvalidConfigurations is true
                logger.AssertLogContains("MSB4204");
            }
            finally
            {
                File.Delete(projectFilePath);

                if (buildManager != null)
                {
                    NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)buildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
                    nodeProviderInProc.Dispose();
                }
            }
        }

        /// <summary>
        /// Bug indicated that when a target framework version greater than 4.0 was used then the solution project generator would crash.
        /// this test is to make sure the fix is not regressed.
        /// </summary>
        [TestMethod]
        public void TestTargetFrameworkVersionGreaterThan4()
        {
            string tmpFileName = FileUtilities.GetTemporaryFile();
            File.Delete(tmpFileName);
            string projectFilePath = tmpFileName + ".sln";

            string solutionFileContents =
               @"
Microsoft Visual Studio Solution File, Format Version 11.00
# Visual Studio 2010
Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'WebSite1', '..\WebSite1\', '{6B8F98F2-C976-4029-9321-5CCD73A174DA}'
	ProjectSection(WebsiteProperties) = preProject
		TargetFrameworkMoniker = '.NETFramework,Version=v4.34'
		Debug.AspNetCompiler.VirtualPath = '/WebSite1'
		Debug.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Debug.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Debug.AspNetCompiler.Updateable = 'true'
		Debug.AspNetCompiler.ForceOverwrite = 'true'
		Debug.AspNetCompiler.FixedNames = 'false'
		Debug.AspNetCompiler.Debug = 'True'
		Release.AspNetCompiler.VirtualPath = '/WebSite1'
		Release.AspNetCompiler.PhysicalPath = '..\WebSite1\'
		Release.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite1\'
		Release.AspNetCompiler.Updateable = 'true'
		Release.AspNetCompiler.ForceOverwrite = 'true'
		Release.AspNetCompiler.FixedNames = 'false'
		Release.AspNetCompiler.Debug = 'False'
		VWDPort = '45602'
		DefaultWebSiteLanguage = 'Visual Basic'
	EndProjectSection
EndProject
Global
	GlobalSection(SolutionConfigurationPlatforms) = preSolution
		Debug|Any CPU = Debug|Any CPU
	EndGlobalSection
	GlobalSection(ProjectConfigurationPlatforms) = postSolution
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.ActiveCfg = Debug|Any CPU
		{6B8F98F2-C976-4029-9321-5CCD73A174DA}.Debug|Any CPU.Build.0 = Debug|Any CPU
	EndGlobalSection
	GlobalSection(SolutionProperties) = preSolution
		HideSolutionNode = FALSE
	EndGlobalSection
EndGlobal
                ";

            try
            {
                MockLogger logger = new MockLogger();

                Dictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties["Configuration"] = "Release";
                globalProperties["SkipInvalidConfigurations"] = "true";

                SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents.Replace('\'', '"'));
                ProjectCollection collection = new ProjectCollection();
                collection.RegisterLogger(logger);

                ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, globalProperties, null, DefaultLicenseValidator.Invalid, collection.LoggingService);

                Version ver = new Version("4.34");
                string message = ResourceUtilities.FormatResourceString("AspNetCompiler.TargetingHigherFrameworksDefaultsTo40", solution.ProjectsInOrder[0].ProjectName, ver.ToString());
                logger.AssertLogContains(message);
            }
            finally
            {
                File.Delete(projectFilePath);
            }
        }

        #region Helper Functions

        /// <summary>
        /// Convert passed in solution file to an MSBuild project. This method is used by Sln2Proj
        /// </summary>
        public bool ConvertSLN2Proj(string nameSolutionFile)
        {
            // Set the environment variable to cause the SolutionProjectGenerator to emit the project to disk
            string oldValueForMSBuildEmitSolution = Environment.GetEnvironmentVariable("MSBuildEmitSolution");
            Environment.SetEnvironmentVariable("MSBuildEmitSolution", "1");

            if (nameSolutionFile == null || !File.Exists(nameSolutionFile))
            {
                return false;
            }

            // Parse the solution
            SolutionFile solution = new SolutionFile();
            solution.FullPath = nameSolutionFile;
            solution.ParseSolutionFile();

            // Generate the in-memory MSBuild project and output it to disk
            ProjectInstance[] instance = SolutionProjectGenerator.Generate(solution, null, null, DefaultLicenseValidator.Invalid, null);


            //Reset the environment variable
            Environment.SetEnvironmentVariable("MSBuildEmitSolution", oldValueForMSBuildEmitSolution);

            return true;
        }

        /// <summary>
        /// Create a Project derived from a Venus solution
        /// </summary>
        private static ProjectInstance CreateVenusSolutionProject()
        {
            return CreateVenusSolutionProject(null, null);
        }

        /// <summary>
        /// Create a Project derived from a Venus solution
        /// </summary>
        private static ProjectInstance CreateVenusSolutionProject(IDictionary<string, string> globalProperties)
        {
            return CreateVenusSolutionProject(globalProperties, null);
        }

        /// <summary>
        /// Create a Project derived from a Venus solution
        /// </summary>
        private static ProjectInstance CreateVenusSolutionProject(string toolsVersion)
        {
            return CreateVenusSolutionProject(null, toolsVersion);
        }

        /// <summary>
        /// Create a Project derived from a Venus solution, given a set of global properties and a ToolsVersion
        /// to use as the override value
        /// </summary>
        /// <param name="globalProperties">The dictionary of global properties.  May be null.</param>
        /// <param name="toolsVersion">The ToolsVersion override value.  May be null.</param>
        private static ProjectInstance CreateVenusSolutionProject(IDictionary<string, string> globalProperties, string toolsVersion)
        {
            string solutionFileContents =
                @"
                Microsoft Visual Studio Solution File, Format Version 9.00
                # Visual Studio 2005
                Project('{E24C65DC-7377-472B-9ABA-BC803B73C61A}') = 'C:\solutions\WebSite2\', '..\..\solutions\WebSite2\', '{F90528C4-6989-4D33-AFE8-F53173597CC2}'
                    ProjectSection(WebsiteProperties) = preProject
                        Debug.AspNetCompiler.VirtualPath = '/WebSite2'
                        Debug.AspNetCompiler.PhysicalPath = '..\..\solutions\WebSite2\'
                        Debug.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite2\'
                        Debug.AspNetCompiler.Updateable = 'true'
                        Debug.AspNetCompiler.ForceOverwrite = 'true'
                        Debug.AspNetCompiler.FixedNames = 'true'
                        Debug.AspNetCompiler.Debug = 'True'
                        Release.AspNetCompiler.VirtualPath = '/WebSite2'
                        Release.AspNetCompiler.PhysicalPath = '..\..\solutions\WebSite2\'
                        Release.AspNetCompiler.TargetPath = 'PrecompiledWeb\WebSite2\'
                        Release.AspNetCompiler.Updateable = 'true'
                        Release.AspNetCompiler.ForceOverwrite = 'true'
                        Release.AspNetCompiler.FixedNames = 'true'
                        Release.AspNetCompiler.Debug = 'False'
                        VWDPort = '2776'
                        DefaultWebSiteLanguage = 'Visual C#'
                    EndProjectSection
                EndProject
                Global
                    GlobalSection(SolutionConfigurationPlatforms) = preSolution
                        Debug|Any CPU = Debug|Any CPU
                    EndGlobalSection
                    GlobalSection(ProjectConfigurationPlatforms) = postSolution
                        {F90528C4-6989-4D33-AFE8-F53173597CC2}.Debug|Any CPU.ActiveCfg = Debug|.NET
                        {F90528C4-6989-4D33-AFE8-F53173597CC2}.Debug|Any CPU.Build.0 = Debug|.NET
                    EndGlobalSection
                EndGlobal
                ";

            SolutionFile solution = SolutionFile_Tests.ParseSolutionHelper(solutionFileContents);

            ProjectInstance[] instances = SolutionProjectGenerator.Generate(solution, globalProperties, toolsVersion, DefaultLicenseValidator.Invalid, null);

            // Index 0 is the traversal project, which will reference the sole Venus project.
            return instances[1];
        }

        /// <summary>
        /// Checks the provided project for a matching itemtype and include value.  If it
        /// does not exist, asserts. 
        /// </summary>
        private void AssertProjectContainsItem(ProjectInstance msbuildProject, string itemType, string include)
        {
            IEnumerable<ProjectItemInstance> itemGroup = msbuildProject.GetItems(itemType);
            Assert.IsNotNull(itemGroup);

            foreach (ProjectItemInstance item in itemGroup)
            {
                if (item.ItemType == itemType && item.EvaluatedInclude == include)
                {
                    return;
                }
            }

            Assert.IsTrue(false);
        }

        /// <summary>
        /// Counts the number of items with a particular itemtype in the provided project, and 
        /// asserts if it doesn't match the provided count.
        /// </summary>
        private void AssertProjectItemNameCount(ProjectInstance msbuildProject, string itemType, int count)
        {
            IEnumerable<ProjectItemInstance> itemGroup = msbuildProject.GetItems(itemType);
            Assert.IsNotNull(itemGroup);
            Assert.AreEqual(count, itemGroup.Count());
        }

        #endregion // Helper Functions
    }
}
