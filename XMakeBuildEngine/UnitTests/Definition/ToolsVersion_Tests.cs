﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;

using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Shared;
using Microsoft.Build.Framework;
using Microsoft.Build.Execution;
using Microsoft.Build.Construction;
using Microsoft.Build.Collections;

using LoggingService = Microsoft.Build.BackEnd.Logging.LoggingService;
using LoggerMode = Microsoft.Build.BackEnd.Logging.LoggerMode;
using InvalidProjectFileException = Microsoft.Build.Exceptions.InvalidProjectFileException;
using InternalUtilities = Microsoft.Build.Internal.Utilities;

namespace Microsoft.Build.UnitTests.Definition
{
    [TestClass]
    public class ToolsetState_Tests
    {
        [TestMethod]
        public void OverrideTasksAreFoundInOverridePath()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection e = new ProjectCollection();
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), "c:\\msbuildoverridetasks", new DirectoryExists(this.directoryExists));

            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            TaskRegistry taskRegistry = (TaskRegistry)t.GetTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);
            TaskRegistry taskoverrideRegistry = (TaskRegistry)t.GetOverrideTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);

            string[] expectedRegisteredTasks = { "a1", "a2", "a3", "a4", "b1", "e1", "g1", "g2", "g3" };
            string[] expectedOverrideTasks = { "a1" /* special because it is in the override tasks file as well as in the tasks file*/, "oa1", "oa2", "og1", "ooo" };

            string[] unexpectedRegisteredTasks = { "c1", "d1", "f1", "11", "12", "13", "21", "oa1", "oa2", "og1", "ooo" };
            string[] unexpectedOverrideRegisteredTasks = { "c1", "d1", "f1", "11", "12", "13", "21", "a2", "a3", "a4", "b1", "e1", "g1", "g2", "g3" };

            foreach (string expectedRegisteredTask in expectedRegisteredTasks)
            {
                Assert.IsTrue(taskRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(expectedRegisteredTask, null)),
                              String.Format("Expected task '{0}' registered!", expectedRegisteredTask));
            }

            foreach (string expectedRegisteredTask in expectedOverrideTasks)
            {
                Assert.IsTrue(taskoverrideRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(expectedRegisteredTask, null)),
                              String.Format("Expected task '{0}' registered!", expectedRegisteredTask));
            }

            foreach (string unexpectedRegisteredTask in unexpectedRegisteredTasks)
            {
                Assert.IsFalse(taskRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(unexpectedRegisteredTask, null)),
                              String.Format("Unexpected task '{0}' registered!", unexpectedRegisteredTask));
            }

            foreach (string unexpectedRegisteredTask in unexpectedOverrideRegisteredTasks)
            {
                Assert.IsFalse(taskoverrideRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(unexpectedRegisteredTask, null)),
                              String.Format("Unexpected task '{0}' registered!", unexpectedRegisteredTask));
            }
        }

        [TestMethod]
        public void OverrideTaskPathIsRelative()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection e = new ProjectCollection();
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), "msbuildoverridetasks", new DirectoryExists(this.directoryExists));

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            TaskRegistry taskoverrideRegistry = (TaskRegistry)t.GetOverrideTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);
            Assert.IsNotNull(taskoverrideRegistry);
            Assert.IsTrue(taskoverrideRegistry.TaskRegistrations.Count == 0);
            string rootedPathMessage = ResourceUtilities.FormatResourceString("OverrideTaskNotRootedPath", "msbuildoverridetasks");
            mockLogger.AssertLogContains(ResourceUtilities.FormatResourceString("OverrideTasksFileFailure", rootedPathMessage));
        }

        [TestMethod]
        public void OverrideTaskPathHasInvalidChars()
        {
            ProjectCollection e = new ProjectCollection();
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), "k:\\||^%$#*msbuildoverridetasks", new DirectoryExists(this.directoryExists));

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            TaskRegistry taskoverrideRegistry = (TaskRegistry)t.GetOverrideTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);
            Assert.IsNotNull(taskoverrideRegistry);
            Assert.IsTrue(taskoverrideRegistry.TaskRegistrations.Count == 0);
            mockLogger.AssertLogContains("MSB4194");
        }

        [TestMethod]
        public void OverrideTaskPathHasTooLongOfAPath()
        {
            string tooLong = "c:\\" + new string('C', 6000);
            ProjectCollection e = new ProjectCollection();
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), tooLong, new DirectoryExists(this.directoryExists));

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            TaskRegistry taskoverrideRegistry = (TaskRegistry)t.GetOverrideTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);
            Assert.IsNotNull(taskoverrideRegistry);
            Assert.IsTrue(taskoverrideRegistry.TaskRegistrations.Count == 0);
            string rootedPathMessage = ResourceUtilities.FormatResourceString("OverrideTaskNotRootedPath", tooLong);
            mockLogger.AssertLogContains(ResourceUtilities.FormatResourceString("OverrideTasksFileFailure", rootedPathMessage));
        }

        [TestMethod]
        public void OverrideTaskPathIsNotFound()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection e = new ProjectCollection();
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), "k:\\Thecatinthehat", new DirectoryExists(this.directoryExists));

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            TaskRegistry taskoverrideRegistry = (TaskRegistry)t.GetOverrideTaskRegistry(service, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), e.ProjectRootElementCache);
            Assert.IsNotNull(taskoverrideRegistry);
            Assert.IsTrue(taskoverrideRegistry.TaskRegistrations.Count == 0);
            string rootedPathMessage = ResourceUtilities.FormatResourceString("OverrideTaskNotRootedPath", "k:\\Thecatinthehat");
            mockLogger.AssertLogContains(ResourceUtilities.FormatResourceString("OverrideTasksFileFailure", rootedPathMessage));
        }

        [TestMethod]
        public void DefaultTasksAreFoundInToolsPath()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), null, new DirectoryExists(this.directoryExists));

            TaskRegistry taskRegistry = (TaskRegistry)t.GetTaskRegistry(null, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), ProjectCollection.GlobalProjectCollection.ProjectRootElementCache);

            string[] expectedRegisteredTasks = { "a1", "a2", "a3", "a4", "b1", "e1", "g1", "g2", "g3" };
            string[] unexpectedRegisteredTasks = { "c1", "d1", "f1", "11", "12", "13", "21" };

            foreach (string expectedRegisteredTask in expectedRegisteredTasks)
            {
                Assert.IsTrue(taskRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(expectedRegisteredTask, null)),
                              String.Format("Expected task '{0}' registered!", expectedRegisteredTask));
            }
            foreach (string unexpectedRegisteredTask in unexpectedRegisteredTasks)
            {
                Assert.IsFalse(taskRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(unexpectedRegisteredTask, null)),
                              String.Format("Unexpected task '{0}' registered!", unexpectedRegisteredTask));
            }
        }

        [TestMethod]
        public void WarningLoggedIfNoDefaultTasksFound()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection p = new ProjectCollection();
            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            Toolset t = new Toolset("toolsversionname", "c:\\directory1\\directory2\\doesntexist", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), null, new DirectoryExists(this.directoryExists));

            TaskRegistry taskRegistry = (TaskRegistry)t.GetTaskRegistry(service, DefaultLicenseValidator.Invalid, ProjectCollection.GlobalProjectCollection.ProjectRootElementCache);

            string[] unexpectedRegisteredTasks = { "a1", "a2", "a3", "a4", "b1", "c1", "d1", "e1", "f1", "g1", "g2", "g3", "11", "12", "13", "21" };

            Assert.AreEqual(1, mockLogger.WarningCount, "Expected 1 warning logged!");
            foreach (string unexpectedRegisteredTask in unexpectedRegisteredTasks)
            {
                Assert.IsFalse(taskRegistry.TaskRegistrations.ContainsKey(new TaskRegistry.RegisteredTaskIdentity(unexpectedRegisteredTask, null)),
                               String.Format("Unexpected task '{0}' registered!", unexpectedRegisteredTask));
            }
        }

        [TestMethod]
        public void InvalidToolPath()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection p = new ProjectCollection();
            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);
            Toolset t = new Toolset("toolsversionname", "invalid||path", new PropertyDictionary<ProjectPropertyInstance>(), p, new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), null, new DirectoryExists(this.directoryExists));

            TaskRegistry taskRegistry = (TaskRegistry)t.GetTaskRegistry(service, DefaultLicenseValidator.Invalid, ProjectCollection.GlobalProjectCollection.ProjectRootElementCache);

            Console.WriteLine(mockLogger.FullLog);
            Assert.AreEqual(1, mockLogger.WarningCount, "Expected a warning for invalid character in toolpath");
        }

        /// <summary>
        /// Make sure when we read in the tasks files off disk that they come in in a sorted order so that there is a deterministric way of 
        /// figurting out the order the files were read in.
        /// </summary>
        [TestMethod]
        public void VerifyTasksFilesAreInSortedOrder()
        {
            //Note Engine's BinPath is distinct from the ToolsVersion's ToolsPath
            ProjectCollection p = new ProjectCollection();
            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);
            string[] foundFiles = Toolset.GetTaskFiles(new DirectoryGetFiles(this.getFiles), service, DefaultLicenseValidator.Invalid, "*.tasks", "c:\\directory1\\directory2", String.Empty);
            string[] foundoverrideFiles = Toolset.GetTaskFiles(new DirectoryGetFiles(this.getFiles), service, DefaultLicenseValidator.Invalid, "*.overridetasks", "c:\\msbuildoverridetasks", String.Empty);

            List<string> sortedTasksExpectedPaths = new List<string>();
            List<string> sortedOverrideExpectedPaths = new List<string>();

            foreach (DefaultTasksFile file in _defaultTasksFileCandidates)
            {
                if (Path.GetDirectoryName(file.Path).Equals("c:\\directory1\\directory2", StringComparison.OrdinalIgnoreCase) && file.Path.EndsWith(".tasks", StringComparison.OrdinalIgnoreCase))
                {
                    sortedTasksExpectedPaths.Add(file.Path);
                }

                if (Path.GetDirectoryName(file.Path).Equals("c:\\msbuildoverridetasks", StringComparison.OrdinalIgnoreCase) && file.Path.EndsWith(".overridetasks", StringComparison.OrdinalIgnoreCase))
                {
                    sortedOverrideExpectedPaths.Add(file.Path);
                }
            }

            sortedTasksExpectedPaths.Sort(StringComparer.OrdinalIgnoreCase);
            sortedOverrideExpectedPaths.Sort(StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(sortedTasksExpectedPaths.Count == foundFiles.Length);
            for (int i = 0; i < foundFiles.Length; i++)
            {
                Assert.IsTrue(sortedTasksExpectedPaths[i].Equals(foundFiles[i], StringComparison.OrdinalIgnoreCase));
            }


            Assert.IsTrue(sortedOverrideExpectedPaths.Count == foundoverrideFiles.Length);
            for (int i = 0; i < foundoverrideFiles.Length; i++)
            {
                Assert.IsTrue(sortedOverrideExpectedPaths[i].Equals(foundoverrideFiles[i], StringComparison.OrdinalIgnoreCase));
            }
        }

        [TestMethod]
        public void InvalidToolsVersionTooHighMappedToCurrent()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");
            string oldTreatHigherToolsVersions = Environment.GetEnvironmentVariable("MSBUILDTREATHIGHERTOOLSVERSIONASCURRENT");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDTREATHIGHERTOOLSVERSIONASCURRENT", "1");
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='98.6' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                        <Target Name='Foo'>
                        </Target>
                       </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);

                mockLogger.AssertLogContains("ToolsVersion=\"98.6\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDTREATHIGHERTOOLSVERSIONASCURRENT", oldTreatHigherToolsVersions);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        [TestMethod]
        public void InvalidToolsVersionMissingLowMappedToCurrent()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='0.1' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"0.1\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        [TestMethod]
        public void InvalidToolsVersionMissingMappedToCurrent()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='invalidToolsVersion' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"invalidToolsVersion\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        [TestMethod]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void InvalidToolsVersion()
        {
            ProjectCollection p = new ProjectCollection();
            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            bool success = false;
            Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='invalidToolsVersion' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, "goober", p);
            success = project.Build(mockLogger);
            // BANG!
        }

        /// <summary>
        /// Even a valid toolsversion should be forced to the current ToolsVersion if MSBUILDTREATALLTOOLSVERSIONSASCURRENT
        /// is set.
        /// </summary>
        [TestMethod]
        public void ToolsVersionMappedToCurrent()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");
            string oldForceToolsVersionToCurrent = Environment.GetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", oldForceToolsVersionToCurrent);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// Validate that a custom defined toolset is honored
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void CustomToolsVersionIsHonored()
        {
            Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", String.Empty);
            try
            {
                string content = @"<Project ToolsVersion=""14.0"" xmlns=""http://schemas.microsoft.com/developer/msbuild/2003"">
    <Target Name=""a"">
        <Message Text=""[$(MSBUILDTOOLSVERSION)]"" />
    </Target>
</Project>
";
                string projectPath = Path.GetTempFileName();
                File.WriteAllText(projectPath, content);

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                Toolset source = p.GetToolset("14.0");
                Toolset potato = new Toolset("potato", source.ToolsPath, ProjectCollection.GlobalProjectCollection, source.ToolsPath);
                p.AddToolset(potato);

                bool success = false;
                Project project = p.LoadProject(projectPath, "potato");
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("[potato]");
            }
            finally
            {
                // Nothing
            }
        }

        /// <summary>
        /// If the current ToolsVersion doesn't exist, we should fall back to what's in the project file. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFallbackIfCurrentToolsVersionDoesNotExist()
        {
            ProjectCollection p = new ProjectCollection();
            p.RemoveToolset(ObjectModelHelpers.MSBuildDefaultToolsVersion);

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            bool success = false;
            Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

            Assert.AreEqual("4.0", project.ToolsVersion);
            success = project.Build(mockLogger);

            Assert.IsTrue(success);
            mockLogger.AssertLogContains("\"4.0\"");
            mockLogger.AssertLogDoesntContain(ObjectModelHelpers.CleanupFileContents("\"msbuilddefaulttoolsversion\""));
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// then if MSBUILDDEFAULTTOOLSVERSION is set and exists, use that ToolsVersion. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFromEnvironmentVariable()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                p.AddToolset(new Toolset("foo", @"c:\foo", p, @"c:\foo\override"));
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains("ToolsVersion=\"foo\"");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// and if MSBUILDDEFAULTTOOLSVERSION is set but to an invalid ToolsVersion, fall back to current. 
        /// </summary>
        [TestMethod]
        public void InvalidToolsVersionFromEnvironmentVariable()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = project.Build(mockLogger);

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                // falls back to the current ToolsVersion
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// Even a valid toolsversion should be forced to the current ToolsVersion if MSBUILDTREATALLTOOLSVERSIONSASCURRENT
        /// is set.
        /// </summary>
        [TestMethod]
        public void ToolsVersionMappedToCurrent_CreateProjectInstance()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");
            string oldForceToolsVersionToCurrent = Environment.GetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = project.CreateProjectInstance();
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", oldForceToolsVersionToCurrent);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// If the current ToolsVersion doesn't exist, we should fall back to what's in the project file. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFallbackIfCurrentToolsVersionDoesNotExist_CreateProjectInstance()
        {
            ProjectCollection p = new ProjectCollection();
            p.RemoveToolset(ObjectModelHelpers.MSBuildDefaultToolsVersion);

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            bool success = false;
            Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

            ProjectInstance pi = project.CreateProjectInstance();
            Assert.AreEqual("4.0", pi.ToolsVersion);
            success = pi.Build(new ILogger[] { mockLogger });

            Assert.IsTrue(success);
            mockLogger.AssertLogContains("\"4.0\"");
            mockLogger.AssertLogDoesntContain(ObjectModelHelpers.CleanupFileContents("\"msbuilddefaulttoolsversion\""));
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// then if MSBUILDDEFAULTTOOLSVERSION is set and exists, use that ToolsVersion. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFromEnvironmentVariable_CreateProjectInstance()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                p.AddToolset(new Toolset("foo", @"c:\foo", p, @"c:\foo\override"));
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = project.CreateProjectInstance();
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains("ToolsVersion=\"foo\"");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// and if MSBUILDDEFAULTTOOLSVERSION is set but to an invalid ToolsVersion, fall back to current. 
        /// </summary>
        [TestMethod]
        public void InvalidToolsVersionFromEnvironmentVariable_CreateProjectInstance()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = project.CreateProjectInstance();
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                // falls back to the current ToolsVersion
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }


        /// <summary>
        /// Even a valid toolsversion should be forced to the current ToolsVersion if MSBUILDTREATALLTOOLSVERSIONSASCURRENT
        /// is set.
        /// </summary>
        [TestMethod]
        public void ToolsVersionMappedToCurrent_ProjectInstance()
        {
            string oldLegacyToolsVersion = Environment.GetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION");
            string oldForceToolsVersionToCurrent = Environment.GetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", "1");
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", "1");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = new ProjectInstance(project.Xml, null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDLEGACYDEFAULTTOOLSVERSION", oldLegacyToolsVersion);
                Environment.SetEnvironmentVariable("MSBUILDTREATALLTOOLSVERSIONSASCURRENT", oldForceToolsVersionToCurrent);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// If the current ToolsVersion doesn't exist, we should fall back to what's in the project file. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFallbackIfCurrentToolsVersionDoesNotExist_ProjectInstance()
        {
            ProjectCollection p = new ProjectCollection();
            p.RemoveToolset(ObjectModelHelpers.MSBuildDefaultToolsVersion);

            MockLogger mockLogger = new MockLogger();
            LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
            service.RegisterLogger(mockLogger);

            bool success = false;
            Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

            ProjectInstance pi = new ProjectInstance(project.Xml, null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
            Assert.AreEqual("4.0", pi.ToolsVersion);
            success = pi.Build(new ILogger[] { mockLogger });

            Assert.IsTrue(success);
            mockLogger.AssertLogContains("\"4.0\"");
            mockLogger.AssertLogDoesntContain(ObjectModelHelpers.CleanupFileContents("\"msbuilddefaulttoolsversion\""));
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// then if MSBUILDDEFAULTTOOLSVERSION is set and exists, use that ToolsVersion. 
        /// </summary>
        [TestMethod]
        public void ToolsVersionFromEnvironmentVariable_ProjectInstance()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                p.AddToolset(new Toolset("foo", @"c:\foo", p, @"c:\foo\override"));
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = new ProjectInstance(project.Xml, null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                mockLogger.AssertLogContains("ToolsVersion=\"foo\"");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// If MSBUILDTREATALLTOOLSVERSIONSASCURRENT is not set, and there is not an explicit ToolsVersion passed to the project, 
        /// and if MSBUILDDEFAULTTOOLSVERSION is set but to an invalid ToolsVersion, fall back to current. 
        /// </summary>
        [TestMethod]
        public void InvalidToolsVersionFromEnvironmentVariable_ProjectInstance()
        {
            string oldDefaultToolsVersion = Environment.GetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION");

            try
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", "foo");
                InternalUtilities.RefreshInternalEnvironmentValues();

                ProjectCollection p = new ProjectCollection();
                MockLogger mockLogger = new MockLogger();
                LoggingService service = (LoggingService)LoggingService.CreateLoggingService(LoggerMode.Synchronous, 1);
                service.RegisterLogger(mockLogger);

                bool success = false;
                Project project = new Project(XmlReader.Create(new StringReader(@"<Project ToolsVersion='4.0' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                    <Target Name='Foo'>
                    </Target>
                   </Project>")), null /* no global properties */, null /* don't explicitly set the toolsversion */, p);

                ProjectInstance pi = new ProjectInstance(project.Xml, null /* no global properties */, null /* don't explicitly set the toolsversion */, p);
                success = pi.Build(new ILogger[] { mockLogger });

                Assert.IsTrue(success);
                mockLogger.AssertLogContains("ToolsVersion=\"4.0\"");
                // falls back to the current ToolsVersion
                mockLogger.AssertLogContains(ObjectModelHelpers.CleanupFileContents("ToolsVersion=\"msbuilddefaulttoolsversion\""));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDDEFAULTTOOLSVERSION", oldDefaultToolsVersion);
                InternalUtilities.RefreshInternalEnvironmentValues();
            }
        }

        /// <summary>
        /// Inline tasks found in a .tasks file only have properties expanded.
        /// (When they are in a regular MSBuild file, items are also expanded.)
        /// </summary>
        [TestMethod]
        public void InlineTasksInDotTasksFile()
        {
            Toolset t = new Toolset("t", "c:\\inline", new PropertyDictionary<ProjectPropertyInstance>(), new ProjectCollection(), new DirectoryGetFiles(this.getFiles), new LoadXmlFromPath(this.loadXmlFromPath), null, new DirectoryExists(directoryExists));

            TaskRegistry taskRegistry = (TaskRegistry)t.GetTaskRegistry(null, new DefaultLicenseValidator(1, 2, DefaultLicenseValidator.InvalidProjectContextId, 4), ProjectCollection.GlobalProjectCollection.ProjectRootElementCache);

            // Did not crash due to trying to expand items without having items      
        }

        public ToolsetState_Tests()
        {
            _defaultTasksFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (DefaultTasksFile defaultTasksFileCandidate in _defaultTasksFileCandidates)
            {
                _defaultTasksFileMap.Add(defaultTasksFileCandidate.Path, defaultTasksFileCandidate.XmlContents);
            }
        }

        private bool directoryExists(string path)
        {
            // run throught directory exits to throw the correct exceptions if there are any
            Directory.Exists(path);
            return path.Contains("msbuildoverridetasks");
        }

        private string[] getFiles(string path, string pattern)
        {
            // Cause an exception if the path is invalid
            Path.GetFileName(path);

            string pathWithoutTrailingSlash = path.EndsWith("\\") ? path.Substring(0, path.Length - 1) : path;
            //NOTE: the Replace calls below are a very minimal attempt to convert a basic, cmd.exe-style wildcard
            //into something Regex.IsMatch will know how to use.
            string finalPattern = "^" + pattern.Replace(".", "\\.").Replace("*", "[\\w\\W]*") + "$";

            List<string> matches = new List<string>(_defaultTasksFileMap.Keys);
            matches.RemoveAll(
                delegate (string candidate)
                {
                    bool sameFolder = (0 == String.Compare(Path.GetDirectoryName(candidate),
                                                           pathWithoutTrailingSlash,
                                                           StringComparison.OrdinalIgnoreCase));
                    return !sameFolder || !Regex.IsMatch(Path.GetFileName(candidate), finalPattern);
                });
            return matches.ToArray();
        }

        private XmlDocumentWithLocation loadXmlFromPath(string path)
        {
            string xmlContents = _defaultTasksFileMap[path];
            XmlDocumentWithLocation xmlDocument = new XmlDocumentWithLocation();
            xmlDocument.LoadXml(xmlContents);
            return xmlDocument;
        }

        private readonly Dictionary<string, string> _defaultTasksFileMap;

        private DefaultTasksFile[] _defaultTasksFileCandidates =
            { new DefaultTasksFile(
                      "c:\\directory1\\directory2\\a.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='a1' AssemblyName='a' />
                            <UsingTask TaskName='a2' AssemblyName='a' />
                            <UsingTask TaskName='a3' AssemblyName='a' />
                            <UsingTask TaskName='a4' AssemblyName='a' />
                       </Project>"),
              new DefaultTasksFile("c:\\directory1\\directory2\\b.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='b1' AssemblyName='b' />
                       </Project>"),
              new DefaultTasksFile("c:\\directory1\\directory2\\c.tasksfile",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='c1' AssemblyName='c' />
                       </Project>"),
              new DefaultTasksFile("c:\\directory1\\directory2\\directory3\\d.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='d1' AssemblyName='d' />
                       </Project>"),
              new DefaultTasksFile("c:\\directory1\\directory2\\e.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='e1' AssemblyName='e' />
                       </Project>"),
              new DefaultTasksFile("d:\\directory1\\directory2\\f.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='f1' AssemblyName='f' />
                       </Project>"),
              new DefaultTasksFile("c:\\directory1\\directory2\\g.custom.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='g1' AssemblyName='g' />
                            <UsingTask TaskName='g2' AssemblyName='g' />
                            <UsingTask TaskName='g3' AssemblyName='g' />
                       </Project>"),
              new DefaultTasksFile("c:\\somepath\\1.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='11' AssemblyName='1' />
                            <UsingTask TaskName='12' AssemblyName='1' />
                            <UsingTask TaskName='13' AssemblyName='1' />
                       </Project>"),
              new DefaultTasksFile("c:\\somepath\\2.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='21' AssemblyName='2' />
                       </Project>"),
              new DefaultTasksFile("c:\\inline\\inlinetasks.tasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='t2' AssemblyName='an' Condition='true' TaskFactory='AssemblyFactory' Runtime='CLR2' Architecture='x86' RequiredRuntime='2.0' RequiredPlatform='x86'>
                                <ParameterGroup>
                                   <MyParameter ParameterType='System.String' Output='true' Required='false'/>
                                </ParameterGroup>
                                <Task>
                                    x
                                </Task>
                            </UsingTask>
                       </Project>"),
              new DefaultTasksFile("c:\\msbuildoverridetasks\\1.overridetasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='a1' AssemblyName='o' />
                            <UsingTask TaskName='oa1' AssemblyName='o' />
                            <UsingTask TaskName='oa2' AssemblyName='o' />
                            <UsingTask TaskName='og1' AssemblyName='o' />
                        </Project>"),
               new DefaultTasksFile("c:\\msbuildoverridetasks\\2.overridetasks",
                      @"<Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
                            <UsingTask TaskName='ooo' AssemblyName='o' />
                        </Project>")
};

        public struct DefaultTasksFile
        {
            public string Path;
            public string XmlContents;
            public DefaultTasksFile(string path, string xmlContents)
            {
                this.Path = path;
                this.XmlContents = xmlContents;
            }
        }
    }
}
