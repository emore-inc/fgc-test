﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Tests for evaluation</summary>
//-----------------------------------------------------------------------

using System;
using System.Xml;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Net;
using System.Threading;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.BackEnd;
using ProjectHelpers = Microsoft.Build.UnitTests.BackEnd.ProjectHelpers;
using InvalidProjectFileException = Microsoft.Build.Exceptions.InvalidProjectFileException;
using Microsoft.Build.Construction;
using Microsoft.Build.Shared;

namespace Microsoft.Build.UnitTests.Evaluation
{
    /// <summary>
    /// Tests mainly for project evaluation
    /// </summary>
    [TestClass]
    public class Evaluator_Tests
    {
        /// <summary>
        /// Cleanup
        /// </summary>
        [TestInitialize]
        public void Setup()
        {
            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
            GC.Collect();
        }

        /// <summary>
        /// Cleanup
        /// </summary>
        [TestCleanup]
        public void TearDown()
        {
            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();
            GC.Collect();
        }

        /// <summary>
        /// Verify Exist condition used in Import or ImportGroup elements will succeed when in-memory project is available inside projectCollection. 
        /// </summary>
        [TestMethod]
        public void VerifyExistsInMemoryProjecs()
        {
            string projXml = ObjectModelHelpers.CleanupFileContents(@"
                                <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                                    <Import Project=""c:\temp\foo.import"" Condition=""Exists('c:\temp\foo.import')""/>
                                    <ImportGroup Condition=""Exists('c:\temp\bar.import')"">
                                          <Import Project=""c:\temp\bar.import"" />
                                    </ImportGroup>
                                </Project>");

            string fooXml = ObjectModelHelpers.CleanupFileContents(@"
                              <Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                     <foo>yes</foo>
                                  </PropertyGroup>
                                </Project>");
            string barXml = ObjectModelHelpers.CleanupFileContents(@"
                                <Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                     <bar>yes</bar>
                                  </PropertyGroup>
                                </Project>");

            // no imports should be loaded
            Project project = new Project(XmlReader.Create(new StringReader(projXml)));
            project.ReevaluateIfNecessary();

            Assert.IsNull(project.GetProperty("foo"));
            Assert.IsNull(project.GetProperty("bar"));

            // add in-memory project c:\temp\foo.import
            Project fooImport = new Project(XmlReader.Create(new StringReader(fooXml)));
            fooImport.FullPath = @"c:\temp\foo.import";

            // force reevaluation
            project.MarkDirty();
            project.ReevaluateIfNecessary();

            // foo should be available now via fooImport
            Assert.IsNotNull(project.GetProperty("foo"));
            Assert.IsNull(project.GetProperty("bar"));

            // add in-memory project c:\temp\bar.import
            Project barImport = new Project(XmlReader.Create(new StringReader(barXml)));
            barImport.FullPath = @"c:\temp\bar.import";

            // force reevaluation
            project.MarkDirty();
            project.ReevaluateIfNecessary();

            // both foo and bar should be available
            Assert.IsNotNull(project.GetProperty("foo"));
            Assert.IsNotNull(project.GetProperty("bar"));

            // remove the imports from PRE
            fooImport.FullPath = @"c:\temp\alteredfoo.import";
            barImport.FullPath = @"c:\temp\alteredbar.import";

            // force reevaluation
            project.MarkDirty();
            project.ReevaluateIfNecessary();

            // both foo and bar should be gone
            Assert.IsNull(project.GetProperty("foo"));
            Assert.IsNull(project.GetProperty("bar"));
        }

        /// <summary>
        /// Verify when the conditions are evaluated outside of a target that they are evaluated relative to the file they are physically contained in, 
        /// in the case of Imports, and ImportGroups, and PropertyGroups, but that property conditions are evaluated relative to the project file. 
        /// When conditions are evaluated inside of a target they are evaluated relative to the project file.
        /// 
        /// File Structure
        /// test.targets
        /// test.tx, file to check for existance
        /// subdir\test.proj
        /// </summary>
        [TestMethod]
        public void VerifyConditionsInsideOutsideTargets()
        {
            string testtargets = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                  <PropertyGroup>
                                      <PropertyOutsideTarget Condition=""Exists('test.txt')"">test.txt</PropertyOutsideTarget>
                                      <PropertyOutsideTarget Condition=""Exists('..\test.txt')"">..\test.txt</PropertyOutsideTarget>
                                  </PropertyGroup>
                                  <PropertyGroup Condition=""Exists('test.txt')"">
                                      <PropertyGroupOutsideTarget>test.txt</PropertyGroupOutsideTarget>
                                  </PropertyGroup>
                                  <PropertyGroup Condition=""Exists('..\test.txt')"">
                                      <PropertyGroupOutsideTarget>..\test.txt</PropertyGroupOutsideTarget>
                                  </PropertyGroup>
                                  <Target Name=""Test"">
                                      <PropertyGroup>
                                          <PropertyInsideTarget Condition=""Exists('test.txt')"">test.txt</PropertyInsideTarget>
                                          <PropertyInsideTarget Condition=""Exists('..\test.txt')"">..\test.txt</PropertyInsideTarget>
                                      </PropertyGroup>
                                      <PropertyGroup Condition=""Exists('test.txt')"">
                                          <PropertyGroupInsideTarget>test.txt</PropertyGroupInsideTarget>
                                      </PropertyGroup>
                                      <PropertyGroup Condition=""Exists('..\test.txt')"">
                                          <PropertyGroupInsideTarget>..\test.txt</PropertyGroupInsideTarget>
                                      </PropertyGroup>
                                      <Message Text=""PropertyOutsideTarget: $(PropertyOutsideTarget)"" />
                                      <Message Text=""PropertyGroupOutsideTarget: $(PropertyGroupOutsideTarget)"" />
                                      <Message Text=""PropertyInsideTarget: $(PropertyInsideTarget)"" />
                                      <Message Text=""PropertyGroupInsideTarget: $(PropertyGroupInsideTarget)"" />
                                   </Target>
                                      <Import Project=""projdir.targets"" Condition=""Exists('projdir.targets')"" />
                                      <Import Project=""targetdir.targets"" Condition=""Exists('targetdir.targets')"" />
                                      <ImportGroup Condition=""Exists('projdir2.targets')"">
                                          <Import Project=""projdir2.targets"" />
                                      </ImportGroup>
                                      <ImportGroup Condition=""Exists('targetdir2.targets')"">
                                          <Import Project=""targetdir2.targets"" />
                                      </ImportGroup>
                                   </Project>";

            string projDirTargets = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest"" AfterTargets=""Test"">
                                           <Message Text=""[ProjectDirectoryTargetsImport]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string projDirTargets2 = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest2"" AfterTargets=""Test"">
                                           <Message Text=""[ProjectDirectoryTargetsImportGroup]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string targetDirTargets = @"
                             <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest3"" AfterTargets=""Test"">
                                           <Message Text=""[TargetDirectoryTargetsImport]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string targetDirTargets2 = @"
                             <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest4"" AfterTargets=""Test"">
                                           <Message Text=""[TargetDirectoryTargetsImportGroup]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string subdirTestProj = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Import Project=""..\test.targets"" />
                                </Project>";

            string testTxt = @"Hello";

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "VerifyConditionsInsideOutsideTargets");
            string subDirectory = Path.Combine(targetDirectory, "subdir");

            string testTargetPath = Path.Combine(targetDirectory, "test.targets");
            string targetDirectoryTargetsPath = Path.Combine(targetDirectory, "targetdir.targets");
            string targetDirectoryTargetsPath2 = Path.Combine(targetDirectory, "targetdir2.targets");
            string subdirProjPath = Path.Combine(subDirectory, "test.proj");
            string projectDirectoryTargetsPath = Path.Combine(subDirectory, "projdir.targets");
            string projectDirectoryTargetsPath2 = Path.Combine(subDirectory, "projdir2.targets");
            string textTextPath = Path.Combine(targetDirectory, "test.txt");

            try
            {
                Directory.CreateDirectory(subDirectory);
                File.WriteAllText(testTargetPath, ObjectModelHelpers.CleanupFileContents(testtargets));
                File.WriteAllText(subdirProjPath, ObjectModelHelpers.CleanupFileContents(subdirTestProj));
                File.WriteAllText(textTextPath, testTxt);
                File.WriteAllText(targetDirectoryTargetsPath, ObjectModelHelpers.CleanupFileContents(targetDirTargets));
                File.WriteAllText(targetDirectoryTargetsPath2, ObjectModelHelpers.CleanupFileContents(targetDirTargets2));
                File.WriteAllText(projectDirectoryTargetsPath, ObjectModelHelpers.CleanupFileContents(projDirTargets));
                File.WriteAllText(projectDirectoryTargetsPath2, ObjectModelHelpers.CleanupFileContents(projDirTargets2));

                Project project = new Project(subdirProjPath);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.IsTrue(result);
                logger.AssertLogContains("PropertyOutsideTarget: ..\\test.txt");
                logger.AssertLogContains("PropertyGroupOutsideTarget: test.txt");
                logger.AssertLogContains("PropertyInsideTarget: ..\\test.txt");
                logger.AssertLogContains("PropertyGroupInsideTarget: ..\\test.txt");
                logger.AssertLogContains("[TargetDirectoryTargetsImport]");
                logger.AssertLogDoesntContain("[ProjectDirectoryTargetsImport]");
                logger.AssertLogContains("[TargetDirectoryTargetsImportGroup]");
                logger.AssertLogDoesntContain("[ProjectDirectoryTargetsImportGroup]");
            }
            finally
            {
                FileUtilities.DeleteDirectoryNoThrow(targetDirectory, true);
            }
        }

        /// <summary>
        /// Verify when the conditions are evaluated outside of a target that they are evaluated relative to the file they are physically contained in, 
        /// in the case of Imports, and ImportGroups, and PropertyGroups, but that property conditions are evaluated relative to the project file. 
        /// When conditions are evaluated inside of a target they are evaluated relative to the project file.
        /// 
        /// File Structure
        /// test.targets
        /// test.tx, file to check for existance
        /// subdir\test.proj
        /// </summary>
        [TestMethod]
        public void VerifyConditionsInsideOutsideTargets_ProjectInstance()
        {
            string testtargets = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                  <PropertyGroup>
                                      <PropertyOutsideTarget Condition=""Exists('test.txt')"">test.txt</PropertyOutsideTarget>
                                      <PropertyOutsideTarget Condition=""Exists('..\test.txt')"">..\test.txt</PropertyOutsideTarget>
                                  </PropertyGroup>
                                  <PropertyGroup Condition=""Exists('test.txt')"">
                                      <PropertyGroupOutsideTarget>test.txt</PropertyGroupOutsideTarget>
                                  </PropertyGroup>
                                  <PropertyGroup Condition=""Exists('..\test.txt')"">
                                      <PropertyGroupOutsideTarget>..\test.txt</PropertyGroupOutsideTarget>
                                  </PropertyGroup>
                                  <Target Name=""Test"">
                                      <PropertyGroup>
                                          <PropertyInsideTarget Condition=""Exists('test.txt')"">test.txt</PropertyInsideTarget>
                                          <PropertyInsideTarget Condition=""Exists('..\test.txt')"">..\test.txt</PropertyInsideTarget>
                                      </PropertyGroup>
                                      <PropertyGroup Condition=""Exists('test.txt')"">
                                          <PropertyGroupInsideTarget>test.txt</PropertyGroupInsideTarget>
                                      </PropertyGroup>
                                      <PropertyGroup Condition=""Exists('..\test.txt')"">
                                          <PropertyGroupInsideTarget>..\test.txt</PropertyGroupInsideTarget>
                                      </PropertyGroup>
                                      <Message Text=""PropertyOutsideTarget: $(PropertyOutsideTarget)"" />
                                      <Message Text=""PropertyGroupOutsideTarget: $(PropertyGroupOutsideTarget)"" />
                                      <Message Text=""PropertyInsideTarget: $(PropertyInsideTarget)"" />
                                      <Message Text=""PropertyGroupInsideTarget: $(PropertyGroupInsideTarget)"" />
                                   </Target>
                                      <Import Project=""projdir.targets"" Condition=""Exists('projdir.targets')"" />
                                      <Import Project=""targetdir.targets"" Condition=""Exists('targetdir.targets')"" />
                                      <ImportGroup Condition=""Exists('projdir2.targets')"">
                                          <Import Project=""projdir2.targets"" />
                                      </ImportGroup>
                                      <ImportGroup Condition=""Exists('targetdir2.targets')"">
                                          <Import Project=""targetdir2.targets"" />
                                      </ImportGroup>
                                   </Project>";

            string projDirTargets = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest"" AfterTargets=""Test"">
                                           <Message Text=""[ProjectDirectoryTargetsImport]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string projDirTargets2 = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest2"" AfterTargets=""Test"">
                                           <Message Text=""[ProjectDirectoryTargetsImportGroup]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string targetDirTargets = @"
                             <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest3"" AfterTargets=""Test"">
                                           <Message Text=""[TargetDirectoryTargetsImport]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string targetDirTargets2 = @"
                             <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Target Name=""AfterTest4"" AfterTargets=""Test"">
                                           <Message Text=""[TargetDirectoryTargetsImportGroup]"" Importance=""High""/>
                                    </Target>
                                </Project>";

            string subdirTestProj = @"
                                <Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`msbuildnamespace`>
                                    <Import Project=""..\test.targets"" />
                                </Project>";

            string testTxt = @"Hello";

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "VerifyConditionsInsideOutsideTargets");
            string subDirectory = Path.Combine(targetDirectory, "subdir");

            string testTargetPath = Path.Combine(targetDirectory, "test.targets");
            string targetDirectoryTargetsPath = Path.Combine(targetDirectory, "targetdir.targets");
            string targetDirectoryTargetsPath2 = Path.Combine(targetDirectory, "targetdir2.targets");
            string subdirProjPath = Path.Combine(subDirectory, "test.proj");
            string projectDirectoryTargetsPath = Path.Combine(subDirectory, "projdir.targets");
            string projectDirectoryTargetsPath2 = Path.Combine(subDirectory, "projdir2.targets");
            string textTextPath = Path.Combine(targetDirectory, "test.txt");

            try
            {
                Directory.CreateDirectory(subDirectory);
                File.WriteAllText(testTargetPath, ObjectModelHelpers.CleanupFileContents(testtargets));
                File.WriteAllText(subdirProjPath, ObjectModelHelpers.CleanupFileContents(subdirTestProj));
                File.WriteAllText(textTextPath, testTxt);
                File.WriteAllText(targetDirectoryTargetsPath, ObjectModelHelpers.CleanupFileContents(targetDirTargets));
                File.WriteAllText(targetDirectoryTargetsPath2, ObjectModelHelpers.CleanupFileContents(targetDirTargets2));
                File.WriteAllText(projectDirectoryTargetsPath, ObjectModelHelpers.CleanupFileContents(projDirTargets));
                File.WriteAllText(projectDirectoryTargetsPath2, ObjectModelHelpers.CleanupFileContents(projDirTargets2));

                ProjectInstance project = new ProjectInstance(subdirProjPath);

                MockLogger logger = new MockLogger();
                bool result = project.Build(new ILogger[] { logger });
                Assert.IsTrue(result);
                logger.AssertLogContains("PropertyOutsideTarget: ..\\test.txt");
                logger.AssertLogContains("PropertyGroupOutsideTarget: test.txt");
                logger.AssertLogContains("PropertyInsideTarget: ..\\test.txt");
                logger.AssertLogContains("PropertyGroupInsideTarget: ..\\test.txt");
                logger.AssertLogContains("[TargetDirectoryTargetsImport]");
                logger.AssertLogDoesntContain("[ProjectDirectoryTargetsImport]");
                logger.AssertLogContains("[TargetDirectoryTargetsImportGroup]");
                logger.AssertLogDoesntContain("[ProjectDirectoryTargetsImportGroup]");
            }
            finally
            {
                FileUtilities.DeleteDirectoryNoThrow(targetDirectory, true);
            }
        }

        /// <summary>
        /// When properties are consumed and set in imports make sure that we get the correct warnigns.
        /// </summary>
        [TestMethod]
        public void VerifyUsedUnInitializedPropertyInImports()
        {
            string targetA = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                     <Foo>$(bar)</Foo>
                                  </PropertyGroup>
                                </Project>
                            ");

            string targetB = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                     <bar>Something</bar>
                                  </PropertyGroup>
                                </Project>
                               ");

            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns=""msbuildnamespace"">
                                    <Import Project=""targetA.targets"" />
                                    <Import Project=""targetB.targets"" />
                                    <Target Name=""Build""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "VerifyUsedUnInitializedPropertyInImports");

            string targetAPath = Path.Combine(targetDirectory, "targetA.targets");
            string targetBPath = Path.Combine(targetDirectory, "targetB.targets");
            string projectPath = Path.Combine(targetDirectory, "test.proj");
            bool originalValue = BuildParameters.WarnOnUninitializedProperty;
            try
            {
                BuildParameters.WarnOnUninitializedProperty = true;
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(targetAPath, targetA);
                File.WriteAllText(targetBPath, targetB);
                File.WriteAllText(projectPath, projectContents);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(projectPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogContains("MSB4211");
            }
            finally
            {
                BuildParameters.WarnOnUninitializedProperty = originalValue;
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If a property is set to an empty value and then set to a non empty value we do not expect a warning. 
        /// </summary>
        [TestMethod]
        public void EmptyPropertyIsThenSet()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                      <bar></bar>
                                      <baz>$(bar)</baz>
                                      <bar>Something</bar>
                                  </PropertyGroup>
                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "EmptyPropertyIsThenSet");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            string originalValue = Environment.GetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY");
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", "true");
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogDoesntContain("MSB4211");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", originalValue);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If a property is set to an empty value and the environment variable is not set then we do not expect an error
        /// </summary>
        [TestMethod]
        public void EmptyPropertyIsThenSetEnvironmentVariableNotSet()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                      <baz>$(bar)</baz>
                                      <bar>Something</bar>
                                  </PropertyGroup>
                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "EmptyPropertyIsThenSet");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            string originalValue = Environment.GetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY");
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", null);
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogDoesntContain("MSB4211");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", originalValue);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If a property has not been set yet and we consume the property we are setting in order to set it, do not warn 
        /// </summary>
        [TestMethod]
        public void SetPropertyToItself()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                  <PropertyGroup>
                                      <baz>$(baz);I am some text</baz>
                                      <bar>STUFF $(baz) STUFF</bar>
                                  </PropertyGroup>
                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "SetPropertyToItself");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            string originalValue = Environment.GetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY");
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", "true");
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogDoesntContain("MSB4211");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", originalValue);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If we consume a property which has not been initialized in a property we do not expect a warning because we are explicitly ignoring conditions.
        /// This is done because it is a very common scenario to use uninitialized properties in conditions to set default values.
        /// </summary>
        [TestMethod]
        public void UsePropertyInCondition()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                    <PropertyGroup Condition=""'$(Maz)' == ''"">
                                       <Maz>Something</Maz>
                                     </PropertyGroup>
                                     <PropertyGroup>
                                         <Maz Condition=""'$(Maz)' == ''"">Something</Maz>        
                                     </PropertyGroup>

                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "UsePropertyInCondition");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            string originalValue = Environment.GetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY");
            try
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", "true");
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogDoesntContain("MSB4211");
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDWARNONUNINITIALIZEDPROPERTY", originalValue);
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If a property is consumed before it is initialized for the first time log a warning.
        /// </summary>
        [TestMethod]
        public void UsePropertyBeforeSet()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                     <PropertyGroup>
                                         <Foo>$(baz) $(bar)</Foo>
                                         <bar>Something</bar>
                                         <baz>Something</baz>   
                                     </PropertyGroup>

                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "UsePropertyBeforeSet");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            bool originalValue = BuildParameters.WarnOnUninitializedProperty;
            try
            {
                BuildParameters.WarnOnUninitializedProperty = true;
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogContains("MSB4211");
                Assert.IsTrue(logger.WarningCount == 2, "Expected two warnings");
            }
            finally
            {
                BuildParameters.WarnOnUninitializedProperty = originalValue;
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// If we use a property twice make sure we warn and dont crash due to the dictionary which is holding the used but uninitialized variables..
        /// </summary>
        [TestMethod]
        public void UsePropertyBeforeSetDuplicates()
        {
            string testtargets = ObjectModelHelpers.CleanupFileContents(@"
                                <Project xmlns='msbuildnamespace'>
                                     <PropertyGroup>
                                         <Foo1>$(baz) $(bar)</Foo1>
                                         <Foo>$(baz) $(bar)</Foo>
                                         <bar>Something</bar>
                                         <baz>Something</baz>   
                                     </PropertyGroup>

                                  <Target Name=""Test""/>
                                </Project>");

            string tempPath = Path.GetTempPath();
            string targetDirectory = Path.Combine(tempPath, "UsePropertyBeforeSetDuplicates");
            string testTargetPath = Path.Combine(targetDirectory, "test.proj");

            bool originalValue = BuildParameters.WarnOnUninitializedProperty;
            try
            {
                BuildParameters.WarnOnUninitializedProperty = true;
                Directory.CreateDirectory(targetDirectory);
                File.WriteAllText(testTargetPath, testtargets);

                MockLogger logger = new MockLogger();
                ProjectCollection pc = new ProjectCollection();
                pc.RegisterLogger(logger);
                Project project = pc.LoadProject(testTargetPath);

                bool result = project.Build();
                Assert.AreEqual(true, result);
                logger.AssertLogContains("MSB4211");
                Assert.IsTrue(logger.WarningCount == 2, "Expected two warnings");
            }
            finally
            {
                BuildParameters.WarnOnUninitializedProperty = originalValue;
                Directory.Delete(targetDirectory, true);
            }
        }

        /// <summary>
        /// Imports should only be included once.
        /// A second import should give a warning, and then be ignored.
        /// If it is imported twice, subtle problems will occur: because typically the 2nd import will
        /// have no effect, but occasionally it won't.
        /// </summary>
        [TestMethod]
        public void ImportsOnlyIncludedOnce()
        {
            string importPath = null;

            try
            {
                importPath = FileUtilities.GetTemporaryFile();

                string import = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>$(q)</p>
                        </PropertyGroup>
                    </Project>
                ");

                File.WriteAllText(importPath, import);

                // If the import is pulled in again, the property 'q' will be defined when it is
                // assigned to 'p' in the second inclusion
                string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <q>foo</q>
                        </PropertyGroup>

                        <Import Project='" + importPath + @"'/>
                        
                        <PropertyGroup>
                          <q>foo_bar</q>
                        </PropertyGroup>
                        
                        <Import Project='" + importPath + @"'/>

                        <Target Name='t'>
                          <Message Text='$(p)'/>
                        </Target>
                    </Project>
                ");

                Project project = new Project(XmlReader.Create(new StringReader(content)));

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains("foo");
                logger.AssertLogDoesntContain("foo_bar");
            }
            finally
            {
                File.Delete(importPath);
            }
        }

        /// <summary>
        /// Imports should only be included once. However we should be able to see them in the ImportsIncludingDuplicates property
        /// A second import should give a warning, and still not be added to the Imports property.
        /// </summary>
        [TestMethod]
        public void MultipleImportsVerifyImportsIncludingDuplicates()
        {
            string importPath = null;
            string importPath2 = null;
            string importPath3 = null;

            try
            {
                importPath = FileUtilities.GetTemporaryFile();
                importPath2 = FileUtilities.GetTemporaryFile();
                importPath3 = FileUtilities.GetTemporaryFile();

                string import = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>Hello</p>
                        </PropertyGroup>
                    </Project>
                ");

                string import2 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>Hello</p>
                        </PropertyGroup>  
                      <Import Project='" + importPath3 + @"'/>
                    </Project>
                ");

                File.WriteAllText(importPath, import2);
                File.WriteAllText(importPath2, import2);
                File.WriteAllText(importPath3, import);

                // If the import is pulled in again, the property 'q' will be defined when it is
                // assigned to 'p' in the second inclusion
                string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <q>foo</q>
                        </PropertyGroup>

                        <Import Project='" + importPath + @"'/>
                        
                        <PropertyGroup>
                          <q>foo_bar</q>
                        </PropertyGroup>
                        
                        <Import Project='" + importPath + @"'/>
                        <Import Project='" + importPath2 + @"'/>
                        <Target Name='t'>
                          <Message Text='$(p)'/>
                        </Target>
                    </Project>
                ");

                ProjectCollection pc = new ProjectCollection();
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, null, pc, ProjectLoadSettings.RecordDuplicateButNotCircularImports);
                IList<ResolvedImport> imports = project.Imports;
                IList<ResolvedImport> importsIncludingDuplicates = project.ImportsIncludingDuplicates;
                Assert.IsTrue(imports.Count == 3);
                Assert.IsTrue(importsIncludingDuplicates.Count == 5);
                Assert.IsTrue(!imports[0].IsImported);
                Assert.IsTrue(imports[1].IsImported);
                Assert.IsTrue(!imports[2].IsImported);
            }
            finally
            {
                File.Delete(importPath);
                File.Delete(importPath2);
                File.Delete(importPath3);
            }
        }

        /// <summary>
        /// RecordDuplicateButNotCircularImports should not record circular imports (which do come under the category of "duplicate imports".
        /// </summary>
        [TestMethod]
        public void RecordDuplicateButNotCircularImportsWithCircularImports()
        {
            string importPath1 = null;
            string importPath2 = null;

            try
            {
                importPath1 = FileUtilities.GetTemporaryFile();
                importPath2 = FileUtilities.GetTemporaryFile();

                // "import1" imports "import2" and vice versa.
                string import1 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                      <Import Project='" + importPath2 + @"'/>
                    </Project>
                ");

                // "import1" imports "import2" and vice versa.
                string import2 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>Hello</p>
                        </PropertyGroup>  
                      <Import Project='" + importPath1 + @"'/>
                    </Project>
                ");

                File.WriteAllText(importPath1, import1);
                File.WriteAllText(importPath2, import2);

                // The project file contents.
                string manifest = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <Import Project='" + importPath1 + @"'/>
                    </Project>
                ");

                ProjectCollection pc = new ProjectCollection();
                Project project = new Project(XmlReader.Create(new StringReader(manifest)), null, null, pc, ProjectLoadSettings.RecordDuplicateButNotCircularImports);

                // In the list returned by ImportsIncludingDuplicates, check if there are any imports that are imported by importPath2.
                bool circularImportsAreRecorded = project.ImportsIncludingDuplicates.Any(resolvedImport => String.Equals(resolvedImport.ImportingElement.ContainingProject.FullPath, importPath2, StringComparison.OrdinalIgnoreCase));

                // Even though, the text in importPath2 contains exactly one import, namely importPath1, it should not be recorded since
                // importPath1 introduces a circular dependency when traversing depth-first from the project.
                Assert.IsFalse(circularImportsAreRecorded);
            }
            finally
            {
                File.Delete(importPath1);
                File.Delete(importPath2);
            }
        }

        /// <summary>
        /// RecordDuplicateButNotCircularImports should not record circular imports (which do come under the category of "duplicate imports".
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void RejectCircularImportsWithCircularImports()
        {
            string importPath1 = null;
            string importPath2 = null;

            try
            {
                importPath1 = FileUtilities.GetTemporaryFile();
                importPath2 = FileUtilities.GetTemporaryFile();

                // "import1" imports "import2" and vice versa.
                string import1 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                      <Import Project='" + importPath2 + @"'/>
                    </Project>
                ");

                // "import1" imports "import2" and vice versa.
                string import2 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>Hello</p>
                        </PropertyGroup>  
                      <Import Project='" + importPath1 + @"'/>
                    </Project>
                ");

                File.WriteAllText(importPath1, import1);
                File.WriteAllText(importPath2, import2);

                // The project file contents.
                string manifest = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <Import Project='" + importPath1 + @"'/>
                    </Project>
                ");

                ProjectCollection pc = new ProjectCollection();

                // Because import1 and import2 import each other, there is a circular dependency. And since RejectCircularImports flag is set, the below call
                // should throw.
                Project project = new Project(XmlReader.Create(new StringReader(manifest)), null, null, pc, ProjectLoadSettings.RejectCircularImports);
            }
            finally
            {
                File.Delete(importPath1);
                File.Delete(importPath2);
            }
        }

        /// <summary>
        /// MSBuildDefaultTargets was not getting cleared out between reevaluations.
        /// </summary>
        [TestMethod]
        public void MSBuildDefaultTargets()
        {
            Project project = new Project();
            project.Xml.DefaultTargets = "dt";
            project.ReevaluateIfNecessary();

            Assert.AreEqual("dt", project.GetPropertyValue("msbuildprojectdefaulttargets"));

            project.ReevaluateIfNecessary();

            Assert.AreEqual("dt", project.GetPropertyValue("msbuildprojectdefaulttargets"));

            project.MarkDirty();
            project.ReevaluateIfNecessary();

            Assert.AreEqual("dt", project.GetPropertyValue("msbuildprojectdefaulttargets"));

            project.Xml.DefaultTargets = "dt2";
            project.ReevaluateIfNecessary();

            Assert.AreEqual("dt2", project.GetPropertyValue("msbuildprojectdefaulttargets"));
        }

        /// <summary>
        /// Something like a $ in an import's path should work.
        /// </summary>
        [TestMethod]
        public void EscapableCharactersInImportPath()
        {
            string importPath1 = null;
            string importPath2 = null;
            string projectPath = null;
            string directory = null;
            string directory2 = null;

            try
            {
                directory = Path.Combine(Path.GetTempPath(), "fol$der");
                directory2 = Path.Combine(Path.GetTempPath(), "fol$der\\fol$der2");
                Directory.CreateDirectory(directory2);

                string importPathRelativeEscaped = "fol$(x)$der2\\Escap%3beab$(x)leChar$ac;tersInI*tPa?h";
                string importRelative1 = "fol$der2\\Escap;eableChar$ac;tersInImportPath";
                string importRelative2 = "fol$der2\\Escap;eableChar$ac;tersInI_XXXX_tPath";
                importPath1 = Path.Combine(directory, importRelative1);
                importPath2 = Path.Combine(directory, importRelative2);

                string import1 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <Target Name='t'>
                          <Message Text='[imported1]'/>
                        </Target>
                    </Project>
                ");

                string import2 = ObjectModelHelpers.CleanupFileContents(@"
                    <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace' >
                        <Target Name='t2' DependsOnTargets='t'>
                          <Message Text='[imported2]'/>
                        </Target>
                    </Project>
                ");

                File.WriteAllText(importPath1, import1);
                File.WriteAllText(importPath2, import2);

                projectPath = Path.Combine(directory, "my.proj"); // project path has $ in too
                Project project = new Project();
                project.Save(projectPath);
                project.Xml.AddImport(importPathRelativeEscaped);
                Console.WriteLine(project.Xml.RawXml);
                MockLogger logger = new MockLogger();
                bool result = project.Build("t2", new ILogger[] { logger });

                Assert.AreEqual(true, result);

                logger.AssertLogContains("[imported1]");
                logger.AssertLogContains("[imported2]");
            }
            finally
            {
                File.Delete(importPath1);
                File.Delete(importPath2);
                File.Delete(projectPath);
                Directory.Delete(directory2);
                Directory.Delete(directory);
            }
        }

        /// <summary>
        /// There are several built-in MSBuildProjectXXXX properties like MSBuildProjectFile.
        /// These always refer to the outer project whereever they are evaluated.
        /// We also want MSBuildFileXXXX properties that are similar but have special behavior:
        /// their values vary according to the file they are evaluated in.
        /// </summary>
        [TestMethod]
        public void MSBuildThisFileProperties()
        {
            ProjectRootElement main = ProjectRootElement.Create(@"c:\a\p.proj");
            main.AddImport(@"c:\a\t1.targets");
            main.AddImport(@"c:\a\b\t2.targets");
            main.AddImport(@"c:\t3.targets");
            ProjectTargetElement target0 = main.AddTarget("t0");
            AddPropertyDumpTasks(@"c:\a\p.proj", target0);
            main.InitialTargets = "t0";

            ProjectRootElement import1 = ProjectRootElement.Create(@"c:\a\t1.targets");
            ProjectTargetElement target1 = import1.AddTarget("t1");
            AddPropertyDumpTasks(@"c:\a\t1.targets", target1);
            import1.InitialTargets = "t1";

            ProjectRootElement import2 = ProjectRootElement.Create(@"c:\a\b\t2.targets");
            ProjectTargetElement target2 = import2.AddTarget("t2");
            AddPropertyDumpTasks(@"c:\a\b\t2.targets", target2);
            import2.InitialTargets = "t2";

            ProjectRootElement import3 = ProjectRootElement.Create(@"c:\t3.targets");
            ProjectTargetElement target3 = import3.AddTarget("t3");
            AddPropertyDumpTasks(@"c:\t3.targets", target3);
            import3.InitialTargets = "t3";

            Project project = new Project(main);
            MockLogger logger = new MockLogger();
            project.Build(logger);

            ProjectCollection.GlobalProjectCollection.UnloadAllProjects();

            // For comparison:
            //
            // Project "C:\a\p.proj" on node 1 (default targets).
            //  MSBuildProjectDirectory=           C:\a
            //  MSBuildProjectDirectoryNoRoot=     a
            //  MSBuildProjectFile=                p.proj
            //  MSBuildProjectExtension=           .proj
            //  MSBuildProjectFullPath=            C:\a\p.proj
            //  MSBuildProjectName=                p
            //
            // and at the root, c:\p.proj:
            //
            // MSBuildProjectDirectory=           C:\
            // MSBuildProjectDirectoryNoRoot=
            // MSBuildProjectFile=                a.proj
            // MSBuildProjectExtension=           .proj
            // MSBuildProjectFullPath=            C:\a.proj
            // MSBuildProjectName=                a
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFileDirectory=c:\a\");
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFileDirectoryNoRoot=a\");
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFile=p.proj");
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFileExtension=.proj");
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFileFullPath=c:\a\p.proj");
            logger.AssertLogContains(@"c:\a\p.proj: MSBuildThisFileName=p");

            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFileDirectory=c:\a\");
            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFileDirectoryNoRoot=a\");
            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFile=t1.targets");
            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFileExtension=.targets");
            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFileFullPath=c:\a\t1.targets");
            logger.AssertLogContains(@"c:\a\t1.targets: MSBuildThisFileName=t1");

            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFileDirectory=c:\a\b\");
            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFileDirectoryNoRoot=a\b\");
            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFile=t2.targets");
            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFileExtension=.targets");
            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFileFullPath=c:\a\b\t2.targets");
            logger.AssertLogContains(@"c:\a\b\t2.targets: MSBuildThisFileName=t2");

            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFileDirectory=c:\");
            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFileDirectoryNoRoot=");
            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFile=t3.targets");
            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFileExtension=.targets");
            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFileFullPath=c:\t3.targets");
            logger.AssertLogContains(@"c:\t3.targets: MSBuildThisFileName=t3");
        }

        /// <summary>
        /// Per Orcas/Whidbey, if there are several task parameters that only differ
        /// by case, we just silently take the last one.
        /// </summary>
        [TestMethod]
        public void RepeatedTaskParameters()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <Target Name='t'>
                            <Message Text='1' text='2' TEXT='3'/>
                        </Target>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));
            ProjectInstance instance = project.CreateProjectInstance();

            Assert.AreEqual("3", (Helpers.GetFirst(instance.Targets["t"].Tasks)).GetParameter("Text"));
        }

        /// <summary>
        /// Simple override
        /// </summary>
        [TestMethod]
        public void PropertyPredecessors()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>1</p>
                          <p>2</p>
                          <p Condition='false'>3</p>
                          <p>$(p);2</p>
                        </PropertyGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectProperty property = project.GetProperty("p");

            ProjectPropertyElement xml1 = project.Xml.Properties.First();
            Assert.AreEqual("2;2", property.EvaluatedValue);
            Assert.AreEqual("1", property.Predecessor.Predecessor.EvaluatedValue);
            Assert.AreEqual(true, Object.ReferenceEquals(xml1, property.Predecessor.Predecessor.Xml));
            Assert.AreEqual(null, property.Predecessor.Predecessor.Predecessor);
        }

        /// <summary>
        /// Predecessors and imports
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: In-line Utilities function not found.

        public void PropertyPredecessorsAndImports()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion' >
                        <Import Project='$(MSBuildToolsPath)\microsoft.common.targets'/>
                        <PropertyGroup>
                          <!-- Case insensitive -->
                          <OUTdir>1</OUTdir>
                        </PropertyGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            // Verify the predecessor is the one in the import document
            ProjectRootElement importXml = ProjectRootElement.Open(project.Items.ElementAt(0).Xml.ContainingProject.FullPath);
            ProjectRootElement predecessorXmlRoot = project.GetProperty("outdir").Predecessor.Xml.ContainingProject;

            Assert.AreEqual(true, Object.ReferenceEquals(importXml, predecessorXmlRoot));
        }

        /// <summary>
        /// New properties get a null predecessor until reevaluation
        /// </summary>
        [TestMethod]
        public void PropertyPredecessorsSetProperty()
        {
            // Need an existing property with the same name in an import
            // so there's a potential predecessor but it's not just overwritten
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion' >
                        <Import Project='$(MSBuildToolsPath)\microsoft.common.targets'/>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectProperty property = project.SetProperty("outdir", "x"); // Outdir is set in microsoft.common.targets

            Assert.AreEqual(null, property.Predecessor);
        }

        /// <summary>
        /// Predecessor of item definition is item definition
        /// </summary>
        [TestMethod]
        public void ItemDefinitionPredecessorToItemDefinition()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemDefinitionGroup>
                          <i>
                            <m>1</m>
                            <m>2</m>
                            <m Condition='false'>3</m>
                            <m>%(m);2</m>
                          </i>
                        </ItemDefinitionGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectMetadata metadatum = project.ItemDefinitions["i"].GetMetadata("m");

            Assert.AreEqual("2;2", metadatum.EvaluatedValue);
            Assert.AreEqual("1", metadatum.Predecessor.Predecessor.EvaluatedValue);

            ProjectMetadataElement xml1 = project.Xml.ItemDefinitions.ElementAt(0).Metadata.ElementAt(0);
            Assert.AreEqual(true, Object.ReferenceEquals(xml1, metadatum.Predecessor.Predecessor.Xml));
            Assert.AreEqual(null, metadatum.Predecessor.Predecessor.Predecessor);
        }

        /// <summary>
        /// Newly added item's metadata always has null predecessor until reevaluation
        /// </summary>
        [TestMethod]
        public void NewItemPredecessor()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemDefinitionGroup>
                          <i>
                            <m>m1</m>
                          </i>
                        </ItemDefinitionGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectItem item = project.AddItem("i", "i1")[0];
            ProjectMetadata metadatum = item.SetMetadataValue("m", "m2");

            Assert.AreEqual(null, metadatum.Predecessor);
        }

        /// <summary>
        /// Predecessor of item is item definition
        /// </summary>
        [TestMethod]
        public void ItemDefinitionPredecessorToItem()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemDefinitionGroup>
                          <i>
                            <m>1</m>
                          </i>
                        </ItemDefinitionGroup>
                        <ItemGroup>
                          <i Include='i1'>
                            <m>2;%(m)</m>
                            <x>x</x>
                            <m>3;%(m)</m>
                          </i>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectMetadata metadatum = project.GetItems("i").ElementAt(0).GetMetadata("m");

            Assert.AreEqual("3;2;1", metadatum.EvaluatedValue);
            Assert.AreEqual("2;1", metadatum.Predecessor.EvaluatedValue);
            Assert.AreEqual("1", metadatum.Predecessor.Predecessor.EvaluatedValue);

            ProjectMetadataElement xml1 = project.Xml.ItemDefinitions.ElementAt(0).Metadata.ElementAt(0);
            Assert.AreEqual(true, Object.ReferenceEquals(xml1, metadatum.Predecessor.Predecessor.Xml));

            ProjectMetadataElement xml2 = project.Xml.Items.ElementAt(0).Metadata.ElementAt(0);
            Assert.AreEqual(true, Object.ReferenceEquals(xml2, metadatum.Predecessor.Xml));

            Assert.AreEqual(null, metadatum.Predecessor.Predecessor.Predecessor);
        }

        /// <summary>
        /// Predecessor of item is on the same item.
        /// </summary>
        [TestMethod]
        public void PredecessorOnSameItem()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemGroup>
                          <i Include='i0'>
                            <!-- should not be relevant to i1-->
                            <m>0</m>
                          </i>
                          <i Include='i1'>
                            <m>1</m>
                            <m Condition='false'>2</m>
                            <m>3</m>
                          </i>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectMetadata metadatum = project.GetItems("i").ElementAt(1).GetMetadata("m");

            Assert.AreEqual("3", metadatum.EvaluatedValue);
            Assert.AreEqual("1", metadatum.Predecessor.EvaluatedValue);

            ProjectMetadataElement xml1 = project.Xml.Items.ElementAt(1).Metadata.ElementAt(0);
            Assert.AreEqual(true, Object.ReferenceEquals(xml1, metadatum.Predecessor.Xml));

            Assert.AreEqual(null, metadatum.Predecessor.Predecessor);
        }

        /// <summary>
        /// Predecessor of item is item 
        /// </summary>
        [TestMethod]
        public void ItemPredecessorToItem()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemGroup>
                          <h Include='h1'>
                            <m>1</m>
                          </h>
                          <i Include='@(h)'>
                            <m>2;%(m)</m>
                          </i>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectMetadata metadatum = project.GetItems("i").ElementAt(0).GetMetadata("m");

            Assert.AreEqual("2;1", metadatum.EvaluatedValue);
            Assert.AreEqual("1", metadatum.Predecessor.EvaluatedValue);

            ProjectMetadataElement xml1 = project.Xml.Items.ElementAt(0).Metadata.ElementAt(0);
            Assert.AreEqual(true, Object.ReferenceEquals(xml1, metadatum.Predecessor.Xml));

            Assert.AreEqual(null, metadatum.Predecessor.Predecessor);
        }

        /// <summary>
        /// Predecessor of item is item via transform
        /// </summary>
        [TestMethod]
        public void ItemPredecessorToItemViaTransform()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemGroup>
                          <h Include='h1'>
                            <m>1</m>
                          </h>
                          <i Include=""@(h->'%(identity))"">
                            <m>2;%(m)</m>
                          </i>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            ProjectMetadata metadatum = project.GetItems("i").ElementAt(0).GetMetadata("m");

            Assert.AreEqual("2;", metadatum.EvaluatedValue);
            Assert.AreEqual(null, metadatum.Predecessor);
        }

        /// <summary>
        /// Item predecessors and imports
        /// </summary>
        [TestMethod]
        public void ItemPredecessorsAndImports()
        {
            string file = null;

            try
            {
                ProjectRootElement import = ProjectRootElement.Create();
                import.AddItemDefinition("i").AddMetadata("m", "%(m);m1");

                file = FileUtilities.GetTemporaryFile();
                import.Save(file);

                string content = ObjectModelHelpers.CleanupFileContents(@"
                        <Project xmlns='msbuildnamespace' >
                            <ItemDefinitionGroup>
                              <i>
                                <m>m0</m>
                              </i>
                            </ItemDefinitionGroup>
                            <Import Project='" + file + @"'/>
                            <ItemGroup>
                              <i Include='i1'>
                                <m>%(m);m2</m>
                              </i>
                            </ItemGroup>
                        </Project>");

                Project project = new Project(XmlReader.Create(new StringReader(content)));

                ProjectMetadata predecessor = project.GetItems("i").ElementAt(0).GetMetadata("m").Predecessor;

                Assert.AreEqual(true, Object.ReferenceEquals(import, predecessor.Xml.ContainingProject));
                Assert.AreEqual(true, Object.ReferenceEquals(project.Xml, predecessor.Predecessor.Xml.ContainingProject));
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// Cases where there are no predecessors at all
        /// </summary>
        [TestMethod]
        public void NoPredecessors()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>1</p>
                        </PropertyGroup>
                        <ItemDefinitionGroup>
                          <i>
                            <m>m1</m>
                          </i>
                        </ItemDefinitionGroup>
                        <ItemGroup>
                          <j Include='j1'>
                            <m>$(p)</m>
                          </j>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            Assert.AreEqual(null, project.GetProperty("p").Predecessor);
            Assert.AreEqual(null, project.ItemDefinitions["i"].GetMetadata("m").Predecessor);
            Assert.AreEqual(null, project.GetItems("j").ElementAt(0).GetMetadata("m").Predecessor);
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Simple override
        /// </summary>
        [TestMethod]
        public void AllEvaluatedProperties()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <PropertyGroup>
                          <p>1</p>
                          <p>2</p>
                          <p Condition='false'>3</p>
                          <p>$(p);2</p>
                        </PropertyGroup>
                        <PropertyGroup Condition='false'>
                          <p>3</p>
                        </PropertyGroup>
                        <PropertyGroup>
                          <r>4</r>
                        </PropertyGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            IDictionary<string, ProjectProperty> allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates = new Dictionary<string, ProjectProperty>(StringComparer.OrdinalIgnoreCase);

            // Get all those properties from project.AllEvaluatedProperties which don't have a backing xml. As project.AllEvaluatedProperties
            // is an ordered collection and since such properties necessarily should occur before other properties, we don't need to scan
            // the whole list. 
            // We have to dump it into a dictionary because AllEvaluatedProperties contains duplicates, but we're preparing to Properties, 
            // which doesn't, so we need to make sure that the final value in AllEvaluatedProperties is the one that matches. 
            foreach (ProjectProperty property in project.AllEvaluatedProperties.TakeWhile(property => property.Xml == null))
            {
                allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates[property.Name] = property;
            }

            // All those properties which aren't defined in any file. Examples are global properties, environment properties, etc.
            IEnumerable<ProjectProperty> nonImportedProperties = project.Properties.Where(property => property.Xml == null);

            Assert.AreEqual(allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates.Count, nonImportedProperties.Count());

            // Now check and make sure they all match.  If we get through the entire foreach without triggering an Assert.Fail(), then 
            // they do. 
            foreach (ProjectProperty property in nonImportedProperties)
            {
                ProjectProperty propertyFromAllEvaluated = null;

                if (!allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates.TryGetValue(property.Name, out propertyFromAllEvaluated))
                {
                    Assert.Fail(String.Format("project.Properties contained property {0}, but AllEvaluatedProperties did not.", property.Name));
                }
                else if (!property.Equals(propertyFromAllEvaluated))
                {
                    Assert.Fail(String.Format("The properties in project.Properties and AllEvaluatedProperties for property {0} were different.", property.Name));
                }
            }

            // These are the properties which are defined in some file.
            IEnumerable<ProjectProperty> restOfAllEvaluatedProperties = project.AllEvaluatedProperties.SkipWhile(property => property.Xml == null);

            Assert.AreEqual(4, restOfAllEvaluatedProperties.Count());
            Assert.AreEqual("1", restOfAllEvaluatedProperties.ElementAt(0).EvaluatedValue);
            Assert.AreEqual("2", restOfAllEvaluatedProperties.ElementAt(1).EvaluatedValue);
            Assert.AreEqual("2;2", restOfAllEvaluatedProperties.ElementAt(2).EvaluatedValue);
            Assert.AreEqual("4", restOfAllEvaluatedProperties.ElementAt(3).EvaluatedValue);

            // Verify lists reset on reevaluation
            project.MarkDirty();
            project.ReevaluateIfNecessary();

            restOfAllEvaluatedProperties = project.AllEvaluatedProperties.SkipWhile(property => property.Xml == null);
            Assert.AreEqual(4, restOfAllEvaluatedProperties.Count());
        }

        /// <summary>
        /// All evaluated items
        /// </summary>
        [TestMethod]
        public void AllEvaluatedItems()
        {
            string file = null;

            try
            {
                // Should include imported items
                file = FileUtilities.GetTemporaryFile();
                ProjectRootElement import = ProjectRootElement.Create(file);
                import.AddItem("i", "i10");
                import.Save();

                string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemGroup>
                          <i Include='i1'/>
                          <j Include='j1'>
                            <m>m1</m>
                          </j>
                          <i Include='i2' Condition='false'/>
                          <i Include='i3'/>
                        </ItemGroup>

                        <Choose>
                          <When Condition='false'>
                            <ItemGroup>
                              <i Include='i4'/>                     
                            </ItemGroup>
                          </When>
                          <When Condition='true'>
                            <ItemGroup>
                              <i Include='i1'>                     
                                <m>m2</m>
                              </i>
                            </ItemGroup>
                          </When>
                        </Choose>

                        <Choose>
                          <When Condition='false'/>
                          <Otherwise>
                            <ItemGroup>
                              <i Include='i5'/>                     
                            </ItemGroup>
                          </Otherwise>
                        </Choose>

                        <Import Project='" + file + @"'/>
                    </Project>");

                Project project = new Project(XmlReader.Create(new StringReader(content)));

                Assert.AreEqual(6, project.AllEvaluatedItems.Count());
                Assert.AreEqual("i1", project.AllEvaluatedItems.ElementAt(0).EvaluatedInclude);
                Assert.AreEqual(String.Empty, project.AllEvaluatedItems.ElementAt(0).GetMetadataValue("m"));
                Assert.AreEqual("j1", project.AllEvaluatedItems.ElementAt(1).EvaluatedInclude);
                Assert.AreEqual("m1", project.AllEvaluatedItems.ElementAt(1).GetMetadataValue("m"));
                Assert.AreEqual("i3", project.AllEvaluatedItems.ElementAt(2).EvaluatedInclude);
                Assert.AreEqual("i1", project.AllEvaluatedItems.ElementAt(3).EvaluatedInclude);
                Assert.AreEqual("m2", project.AllEvaluatedItems.ElementAt(3).GetMetadataValue("m"));
                Assert.AreEqual("i5", project.AllEvaluatedItems.ElementAt(4).EvaluatedInclude);
                Assert.AreEqual("i10", project.AllEvaluatedItems.ElementAt(5).EvaluatedInclude);

                // Adds aren't applied until reevaluation
                project.AddItem("i", "i6");
                project.AddItem("i", "i7");
                project.RemoveItem(project.AllEvaluatedItems.ElementAt(1));

                Assert.AreEqual(6, project.AllEvaluatedItems.Count());

                project.MarkDirty();
                project.ReevaluateIfNecessary();

                Assert.AreEqual(7, project.AllEvaluatedItems.Count());
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// Evaluated properties list and imports
        /// </summary>
        [TestMethod]
        public void AllEvaluatedPropertiesAndImports()
        {
            string file = null;

            try
            {
                file = FileUtilities.GetTemporaryFile();
                ProjectRootElement import = ProjectRootElement.Create(file);
                import.AddProperty("p", "0").Condition = "false";
                import.AddProperty("p", "1");
                import.AddProperty("q", "2");
                import.Save();

                string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <Import Project='" + file + @"'/>
                        <PropertyGroup>
                          <p>3</p>
                        </PropertyGroup>
                    </Project>");

                Project project = new Project(XmlReader.Create(new StringReader(content)));

                IDictionary<string, ProjectProperty> allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates = new Dictionary<string, ProjectProperty>(StringComparer.OrdinalIgnoreCase);

                // Get all those properties from project.AllEvaluatedProperties which don't have a backing xml. As project.AllEvaluatedProperties
                // is an ordered collection and since such properties necessarily should occur before other properties, we don't need to scan
                // the whole list.
                // We have to dump it into a dictionary because AllEvaluatedProperties contains duplicates, but we're preparing to Properties, 
                // which doesn't, so we need to make sure that the final value in AllEvaluatedProperties is the one that matches. 
                foreach (ProjectProperty property in project.AllEvaluatedProperties.TakeWhile(property => property.Xml == null))
                {
                    allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates[property.Name] = property;
                }

                // All those properties which aren't defined in any file. Examples are global properties, environment properties, etc.
                IEnumerable<ProjectProperty> nonImportedProperties = project.Properties.Where(property => property.Xml == null);

                Assert.AreEqual(allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates.Count, nonImportedProperties.Count());

                // Now check and make sure they all match.  If we get through the entire foreach without triggering an Assert.Fail(), then 
                // they do. 
                foreach (ProjectProperty property in nonImportedProperties)
                {
                    ProjectProperty propertyFromAllEvaluated = null;

                    if (!allEvaluatedPropertiesWithNoBackingXmlAndNoDuplicates.TryGetValue(property.Name, out propertyFromAllEvaluated))
                    {
                        Assert.Fail(String.Format("project.Properties contained property {0}, but AllEvaluatedProperties did not.", property.Name));
                    }
                    else if (!property.Equals(propertyFromAllEvaluated))
                    {
                        Assert.Fail(String.Format("The properties in project.Properties and AllEvaluatedProperties for property {0} were different.", property.Name));
                    }
                }

                // These are the properties which are defined in some file.
                IEnumerable<ProjectProperty> restOfAllEvaluatedProperties = project.AllEvaluatedProperties.SkipWhile(property => property.Xml == null);

                Assert.AreEqual(3, restOfAllEvaluatedProperties.Count());
                Assert.AreEqual("1", restOfAllEvaluatedProperties.ElementAt(0).EvaluatedValue);
                Assert.AreEqual("2", restOfAllEvaluatedProperties.ElementAt(1).EvaluatedValue);
                Assert.AreEqual("3", restOfAllEvaluatedProperties.ElementAt(2).EvaluatedValue);
            }
            finally
            {
                File.Delete(file);
            }
        }

        /// <summary>
        /// New properties do not appear in the evaluated properties list until reevaluation
        /// </summary>
        [TestMethod]
        public void AllEvaluatedPropertiesSetProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
                        <Import Project='$(MSBuildToolsPath)\microsoft.common.targets'/>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            int initial = project.AllEvaluatedProperties.Count();

            ProjectProperty property = project.SetProperty("p", "1");

            Assert.AreEqual(initial, project.AllEvaluatedProperties.Count());

            project.ReevaluateIfNecessary();

            Assert.AreEqual(initial + 1, project.AllEvaluatedProperties.Count());
        }

        /// <summary>
        /// Two item definitions
        /// </summary>
        [TestMethod]
        public void AllEvaluatedItemDefinitionMetadata()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' ToolsVersion='msbuilddefaulttoolsversion'>
                        <ItemDefinitionGroup>
                          <i>
                            <m>1</m>
                            <n>2</n>
                          </i>
                        </ItemDefinitionGroup>
                        <ItemDefinitionGroup>
                          <i>
                            <m>1</m>
                            <m Condition='false'>3</m>
                            <m>%(m);2</m>
                          </i>
                        </ItemDefinitionGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            Assert.AreEqual(4, project.AllEvaluatedItemDefinitionMetadata.Count());

            Assert.AreEqual("2", project.AllEvaluatedItemDefinitionMetadata.ElementAt(1).EvaluatedValue);
            Assert.AreEqual("1;2", project.AllEvaluatedItemDefinitionMetadata.ElementAt(3).EvaluatedValue);

            // Verify lists are cleared on reevaluation
            Assert.AreEqual(4, project.AllEvaluatedItemDefinitionMetadata.Count());
        }

        /// <summary>
        /// Item's metadata does not appear in AllEvaluatedItemDefinitionMetadata
        /// </summary>
        [TestMethod]
        public void AllEvaluatedItemDefinitionItem()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <ItemGroup>
                          <i Include='i1'>
                            <m>m1</m>
                          </i>
                        </ItemGroup>
                    </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            Assert.AreEqual(0, project.AllEvaluatedItemDefinitionMetadata.Count());
        }

        /// <summary>
        /// Cases where there are no AllEvaluated* at all
        /// </summary>
        [TestMethod]
        public void AllEvaluatedListsExceptPropertiesAreEmpty()
        {
            Project project = new Project();

            // All those properties which aren't defined in any file. Examples are global properties, environment properties, etc.
            IEnumerable<ProjectProperty> nonImportedProperties = project.Properties.Where(property => property.Xml == null);

            // AllEvaluatedProperties intentionally includes duplicates; but if there are any among the non-imported properties, then 
            // our count won't match the above.  
            HashSet<string> allProjectPropertiesNoDuplicateNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (ProjectProperty property in project.AllEvaluatedProperties)
            {
                allProjectPropertiesNoDuplicateNames.Add(property.Name);
            }

            Assert.AreEqual(nonImportedProperties.Count(), allProjectPropertiesNoDuplicateNames.Count);
            Assert.AreEqual(0, project.AllEvaluatedItemDefinitionMetadata.Count());
            Assert.AreEqual(0, project.AllEvaluatedItems.Count());
        }

        /// <summary>
        /// Test imports with wildcards and a relative path.
        /// </summary>
        [TestMethod]
        public void ImportWildcardsRelative()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ImportWildcardsRelative");
            string directory2 = Path.Combine(directory, "sub");
            Directory.CreateDirectory(directory2);
            VerifyImportTargetRelativePath(directory, directory2, new string[] { @"**\*.targets" });
        }

        /// <summary>
        /// Test imports with wildcards and a relative path.
        /// </summary>
        [TestMethod]
        public void ImportWildcardsRelative2()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ImportWildcardsRelative2");
            string directory2 = Path.Combine(directory, "sub");
            Directory.CreateDirectory(directory2);
            VerifyImportTargetRelativePath(directory, directory2, new string[] { directory2 + "\\*.targets", directory + "\\*.targets" });
        }

        /// <summary>
        /// Test imports with wildcards and a relative path.
        /// </summary>
        [TestMethod]
        public void ImportWildcardsRelative3()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ImportWildcardsRelative3");
            string directory2 = Path.Combine(directory, "sub");
            Directory.CreateDirectory(directory2);
            VerifyImportTargetRelativePath(directory, directory2, new string[] { directory2 + "\\..\\*.targets", directory + "\\.\\sub\\*.targets" });
        }

        /// <summary>
        /// Test imports with wildcards and a full path
        /// </summary>
        [TestMethod]
        public void ImportWildcardsFullPath()
        {
            string directory = Path.Combine(Path.GetTempPath(), "ImportWildcardsFullPath");
            string directory2 = Path.Combine(directory, "sub");
            Directory.CreateDirectory(directory2);

            string file1 = Path.Combine(directory, "1.targets");
            string file2 = Path.Combine(directory2, "2.targets");
            string file3 = Path.Combine(directory2, "3.cpp.targets");

            VerifyImportTargetRelativePath(directory, directory2, new string[] { file1, file2, file3 });
        }

        /// <summary>
        /// Don't crash on a particular bad conditional.
        /// </summary>
        [TestMethod]
        public void BadConditional()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' >
                        <Target Name='t' Condition='()'/>
                    </Project>");

            MockLogger mockLogger = new MockLogger();
            List<ILogger> loggerList = new List<ILogger>();
            loggerList.Add(mockLogger);

            Project project = new Project(XmlReader.Create(new StringReader(content)));
            ProjectInstance instance = project.CreateProjectInstance();
            instance.Build(loggerList);

            // Expect an error from the bad condition
            Assert.IsTrue(mockLogger.ErrorCount == 1);
            mockLogger.AssertLogContains("MSB4092");
        }

        /// <summary>
        /// Default targets with empty entries doesn't break
        /// </summary>
        [TestMethod]
        public void DefaultTargetsWithBlanks()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' DefaultTargets='t; ;q; ' >
                        <Target Name='t' Condition='()'/>
                    </Project>");

            MockLogger mockLogger = new MockLogger();
            List<ILogger> loggerList = new List<ILogger>();
            loggerList.Add(mockLogger);

            Project project = new Project(XmlReader.Create(new StringReader(content)));
            ProjectInstance instance = project.CreateProjectInstance();
            Assert.AreEqual(instance.DefaultTargets.Count, 2);
            Assert.AreEqual(instance.DefaultTargets[0], "t");
            Assert.AreEqual(instance.DefaultTargets[1], "q");
        }

        /// <summary>
        /// Initial targets with empty entries doesn't break
        /// </summary>
        [TestMethod]
        public void InitialTargetsWithBlanks()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                    <Project xmlns='msbuildnamespace' InitialTargets='t; ;q; ' >
                        <Target Name='t' />
                        <Target Name='q' />
                    </Project>");

            MockLogger mockLogger = new MockLogger();
            List<ILogger> loggerList = new List<ILogger>();
            loggerList.Add(mockLogger);

            Project project = new Project(XmlReader.Create(new StringReader(content)));
            ProjectInstance instance = project.CreateProjectInstance();
            Assert.AreEqual(instance.InitialTargets.Count, 2);
            Assert.AreEqual(instance.InitialTargets[0], "t");
            Assert.AreEqual(instance.InitialTargets[1], "q");
        }

        /// <summary>
        /// Test that the default value for $(MSBuildExtensionsPath) points to "c:\program files\msbuild" in a 64-bit process
        /// or on a 32-bit machine and "c:\program files (x86)\msbuild" in a 32-bit process on a 64-bit machine. 
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPathDefault_Legacy()
        {
            string specialPropertyName = "MSBuildExtensionsPath";

            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable(specialPropertyName);
            string backupMagicSwitch = Environment.GetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH");
            string targetVar = Environment.GetEnvironmentVariable("Target");
            string numberVar = Environment.GetEnvironmentVariable("0env");
            string msbuildVar = Environment.GetEnvironmentVariable("msbuildtoolsversion");

            try
            {
                // Set an environment variable called MSBuildExtensionsPath to some value, for the purpose
                // of seeing whether our value wins.
                Environment.SetEnvironmentVariable(specialPropertyName, null);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", "1");

                // Need to create a new project collection object in order to pick up the new environment variables.
                Project project = new Project(new ProjectCollection());

                Assert.AreEqual(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles) + @"\MSBuild", project.GetPropertyValue(specialPropertyName));
            }
            finally
            {
                // Restore the original value of the MSBuildExtensionsPath environment variable.
                Environment.SetEnvironmentVariable(specialPropertyName, backupMSBuildExtensionsPath);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", backupMagicSwitch);
                Environment.SetEnvironmentVariable("Target", targetVar);
                Environment.SetEnvironmentVariable("0env", numberVar);
                Environment.SetEnvironmentVariable("msbuildtoolsversion", msbuildVar);
            }
        }

        /// <summary>
        /// Test that the default value for $(MSBuildExtensionsPath) points to the 32-bit Program Files always 
        /// (ie. it should have the same value as MSBuildExtensionsPath32). 
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPathDefault()
        {
            string specialPropertyName = "MSBuildExtensionsPath";
            string specialPropertyName32 = "MSBuildExtensionsPath32";

            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable(specialPropertyName);
            string backupMSBuildExtensionsPath32 = Environment.GetEnvironmentVariable(specialPropertyName32);
            string backupMagicSwitch = Environment.GetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH");
            string targetVar = Environment.GetEnvironmentVariable("Target");
            string numberVar = Environment.GetEnvironmentVariable("0env");
            string msbuildVar = Environment.GetEnvironmentVariable("msbuildtoolsversion");

            try
            {
                // Set any pre-existing environment variables to null, just in case someone had set 
                // MSBuildExtensionsPath or MSBuildExtensionsPath32 explicitly in the environment. 
                Environment.SetEnvironmentVariable(specialPropertyName, null);
                Environment.SetEnvironmentVariable(specialPropertyName32, null);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", null);

                // Need to create a new project collection object in order to pick up the new environment variables.
                Project project = new Project(new ProjectCollection());

                Assert.AreEqual(project.GetPropertyValue(specialPropertyName32), project.GetPropertyValue(specialPropertyName));
            }
            finally
            {
                // Restore the original value of the MSBuildExtensionsPath environment variable.
                Environment.SetEnvironmentVariable(specialPropertyName, backupMSBuildExtensionsPath);
                Environment.SetEnvironmentVariable(specialPropertyName32, backupMSBuildExtensionsPath32);
                Environment.SetEnvironmentVariable("MSBUILDLEGACYEXTENSIONSPATH", backupMagicSwitch);
                Environment.SetEnvironmentVariable("Target", targetVar);
                Environment.SetEnvironmentVariable("0env", numberVar);
                Environment.SetEnvironmentVariable("msbuildtoolsversion", msbuildVar);
            }
        }

        /// <summary>
        /// Test that if I set an environment variable called "MSBuildExtensionPath", that my env var
        /// should win over whatever MSBuild thinks the default is.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPathWithEnvironmentOverride()
        {
            // Save the old copy of the MSBuildExtensionsPath, so we can restore it when the unit test is done.
            string backupMSBuildExtensionsPath = Environment.GetEnvironmentVariable("MSBuildExtensionsPath");

            try
            {
                // Set an environment variable called MSBuildExtensionsPath to some value, for the purpose
                // of seeing whether our value wins.
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath", @"c:\foo\bar");

                // Need to create a new project collection object in order to pick up the new environment variables.
                Project project = new Project(new ProjectCollection());

                Assert.AreEqual(@"c:\foo\bar", project.GetPropertyValue("MSBuildExtensionsPath"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath", backupMSBuildExtensionsPath);
            }
        }

        /// <summary>
        /// Test that if I set a global property called "MSBuildExtensionPath", that my global property
        /// should win over whatever MSBuild thinks the default is.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPathWithGlobalOverride()
        {
            Project project = new Project(new ProjectCollection());

            // Set a global property called MSBuildExtensionsPath to some value, for the purpose
            // of seeing whether our value wins.
            project.SetGlobalProperty("MSBuildExtensionsPath", @"c:\devdiv\vscore\msbuild");
            project.ReevaluateIfNecessary();

            Assert.AreEqual(@"c:\devdiv\vscore\msbuild", project.GetPropertyValue("MSBuildExtensionsPath"));
        }

        /// <summary>
        /// The default value for $(MSBuildExtensionsPath32) should point to "c:\program files (x86)\msbuild" on a 64 bit machine. 
        /// We can't test that unless we are on a 64 bit box, but this test will work on either
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath32Default()
        {
            // On a 64 bit machine we always want to use the program files x86.  If we are running as a 64 bit process then this variable will be set correctly
            // If we are on a 32 bit machine or running as a 32 bit process then this variable will be null and the programFiles variable will be correct.
            string expected = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (String.IsNullOrEmpty(expected))
            {
                // 32 bit box
                expected = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            }

            string extensionsPath32Env = Environment.GetEnvironmentVariable("MSBuildExtensionsPath32");

            try
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", null);
                Project project = new Project(new ProjectCollection());

                Assert.AreEqual(expected + @"\MSBuild", project.GetPropertyValue("MSBuildExtensionsPath32"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", extensionsPath32Env);
            }
        }

        /// <summary>
        /// Set an env var called MSBuildExtensionsPath32 to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath32WithEnvironmentOverride()
        {
            string originalMSBuildExtensionsPath32Value = Environment.GetEnvironmentVariable("MSBuildExtensionsPath32");

            try
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", @"c:\devdiv\vscore\msbuild");
                Project project = new Project(new ProjectCollection());
                string msbuildExtensionsPath32Value = project.GetPropertyValue("MSBuildExtensionsPath32");
                Assert.AreEqual(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath32Value);
            }
            finally
            {
                // And restore the old value
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath32", originalMSBuildExtensionsPath32Value);
            }
        }

        /// <summary>
        /// Set a global property called MSBuildExtensionsPath32 to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath32WithGlobalOverride()
        {
            Project project = new Project(new ProjectCollection());

            project.SetGlobalProperty("MSBuildExtensionsPath32", @"c:\devdiv\vscore\msbuild");
            string msbuildExtensionsPath32Value = project.GetPropertyValue("MSBuildExtensionsPath32");
            Assert.AreEqual(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath32Value);
        }

        /// <summary>
        /// The default value for $(MSBuildExtensionsPath64) should point to "c:\program files\msbuild" on a 64 bit machine, 
        /// and should be empty on a 32-bit machine. 
        /// We can't test that unless we are on a 64 bit box, but this test will work on either
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath64Default()
        {
            string expected = String.Empty;

            // If we are on a 32 bit machine then this variable will be null.
            string programFiles32 = Environment.GetEnvironmentVariable("ProgramFiles(x86)");
            if (!String.IsNullOrEmpty(programFiles32))
            {
                // only set in 32-bit windows on 64-bit machines
                expected = Environment.GetEnvironmentVariable("ProgramW6432");

                if (String.IsNullOrEmpty(expected))
                {
                    // 64-bit window on a 64-bit machine -- ProgramFiles is correct
                    expected = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                }
            }

            if (!String.IsNullOrEmpty(expected))
            {
                expected = expected + @"\MSBuild";
            }

            Project project = new Project();

            Assert.AreEqual(expected, project.GetPropertyValue("MSBuildExtensionsPath64"));
        }

        /// <summary>
        /// Set an env var called MSBuildExtensionsPath64 to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath64WithEnvironmentOverride()
        {
            string originalMSBuildExtensionsPath64Value = Environment.GetEnvironmentVariable("MSBuildExtensionsPath64");

            try
            {
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath64", @"c:\devdiv\vscore\msbuild");
                Project project = new Project(new ProjectCollection());
                string msbuildExtensionsPath64Value = project.GetPropertyValue("MSBuildExtensionsPath64");
                Assert.AreEqual(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath64Value);
            }
            finally
            {
                // And restore the old value
                Environment.SetEnvironmentVariable("MSBuildExtensionsPath64", originalMSBuildExtensionsPath64Value);
            }
        }

        /// <summary>
        /// Set a global property called MSBuildExtensionsPath64 to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void MSBuildExtensionsPath64WithGlobalOverride()
        {
            Project project = new Project(new ProjectCollection());

            project.SetGlobalProperty("MSBuildExtensionsPath64", @"c:\devdiv\vscore\msbuild");
            string msbuildExtensionsPath64Value = project.GetPropertyValue("MSBuildExtensionsPath64");
            Assert.AreEqual(@"c:\devdiv\vscore\msbuild", msbuildExtensionsPath64Value);
        }

        /// <summary>
        /// Verify whether LocalAppData property is set by default in msbuild 
        /// with the path of the OS special LocalApplicationData or ApplicationData folders.
        /// </summary>
        [TestMethod]
        public void LocalAppDataDefault()
        {
            string expected = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (String.IsNullOrEmpty(expected))
            {
                expected = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            }

            Project project = new Project();

            Assert.AreEqual(expected, project.GetPropertyValue("LocalAppData"));
        }

        /// <summary>
        /// Set an env var called LocalAppData to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void LocalAppDataWithEnvironmentOverride()
        {
            string originalLocalAppDataValue = Environment.GetEnvironmentVariable("LocalAppData");

            try
            {
                Environment.SetEnvironmentVariable("LocalAppData", @"c:\AppData\Local");
                Project project = new Project(new ProjectCollection());
                string localAppDataValue = project.GetPropertyValue("LocalAppData");
                Assert.AreEqual(@"c:\AppData\Local", localAppDataValue);
            }
            finally
            {
                // And restore the old value
                Environment.SetEnvironmentVariable("LocalAppData", originalLocalAppDataValue);
            }
        }

        /// <summary>
        /// Set a global property called LocalAppData to some value, for the purpose
        /// of seeing whether our value wins.
        /// </summary>
        [TestMethod]
        public void LocalAppDataWithGlobalOverride()
        {
            Project project = new Project(new ProjectCollection());

            project.SetGlobalProperty("LocalAppData", @"c:\AppData\Local");
            string localAppDataValue = project.GetPropertyValue("LocalAppData");
            Assert.AreEqual(@"c:\AppData\Local", localAppDataValue);
        }

        /// <summary>
        /// Test standard reserved properties
        /// </summary>
        [TestMethod]
        public void ReservedProjectProperties()
        {
            string file = @"c:\foo\bar.csproj";
            ProjectRootElement xml = ProjectRootElement.Create(file);
            xml.DefaultTargets = "Build";
            Project project = new Project(xml);

            Assert.AreEqual(@"c:\foo", project.GetPropertyValue("MSBuildProjectDirectory"));
            Assert.AreEqual(@"foo", project.GetPropertyValue("MSBuildProjectDirectoryNoRoot"));
            Assert.AreEqual("bar.csproj", project.GetPropertyValue("MSBuildProjectFile"));
            Assert.AreEqual(".csproj", project.GetPropertyValue("MSBuildProjectExtension"));
            Assert.AreEqual(@"c:\foo\bar.csproj", project.GetPropertyValue("MSBuildProjectFullPath"));
            Assert.AreEqual("bar", project.GetPropertyValue("MSBuildProjectName"));
        }

        /// <summary>
        /// Test standard reserved properties
        /// </summary>
        [TestMethod]
        public void ReservedProjectPropertiesAtRoot()
        {
            string file = @"c:\bar.csproj";
            ProjectRootElement xml = ProjectRootElement.Create(file);
            Project project = new Project(xml);

            Assert.AreEqual(@"c:\", project.GetPropertyValue("MSBuildProjectDirectory"));
            Assert.AreEqual(String.Empty, project.GetPropertyValue("MSBuildProjectDirectoryNoRoot"));
            Assert.AreEqual("bar.csproj", project.GetPropertyValue("MSBuildProjectFile"));
            Assert.AreEqual(".csproj", project.GetPropertyValue("MSBuildProjectExtension"));
            Assert.AreEqual(@"c:\bar.csproj", project.GetPropertyValue("MSBuildProjectFullPath"));
            Assert.AreEqual("bar", project.GetPropertyValue("MSBuildProjectName"));
        }

        /// <summary>
        /// Test standard reserved properties on UNC at root
        /// </summary>
        [TestMethod]
        public void ReservedProjectPropertiesOnUNCRoot()
        {
            string uncFile = @"\\foo\bar\baz.csproj";
            ProjectRootElement xml = ProjectRootElement.Create(uncFile);
            Project project = new Project(xml);

            Assert.AreEqual(@"\\foo\bar", project.GetPropertyValue("MSBuildProjectDirectory"));
            Assert.AreEqual(String.Empty, project.GetPropertyValue("MSBuildProjectDirectoryNoRoot"));
            Assert.AreEqual("baz.csproj", project.GetPropertyValue("MSBuildProjectFile"));
            Assert.AreEqual(".csproj", project.GetPropertyValue("MSBuildProjectExtension"));
            Assert.AreEqual(@"\\foo\bar\baz.csproj", project.GetPropertyValue("MSBuildProjectFullPath"));
            Assert.AreEqual("baz", project.GetPropertyValue("MSBuildProjectName"));
        }

        /// <summary>
        /// Test standard reserved properties on UNC
        /// </summary>
        [TestMethod]
        public void ReservedProjectPropertiesOnUNC()
        {
            string uncFile = @"\\foo\bar\baz\biz.csproj";
            ProjectRootElement xml = ProjectRootElement.Create(uncFile);
            Project project = new Project(xml);

            Assert.AreEqual(@"\\foo\bar\baz", project.GetPropertyValue("MSBuildProjectDirectory"));
            Assert.AreEqual(@"baz", project.GetPropertyValue("MSBuildProjectDirectoryNoRoot"));
            Assert.AreEqual("biz.csproj", project.GetPropertyValue("MSBuildProjectFile"));
            Assert.AreEqual(".csproj", project.GetPropertyValue("MSBuildProjectExtension"));
            Assert.AreEqual(@"\\foo\bar\baz\biz.csproj", project.GetPropertyValue("MSBuildProjectFullPath"));
            Assert.AreEqual("biz", project.GetPropertyValue("MSBuildProjectName"));
        }

        /// <summary>
        /// Verify when a node count is passed through on the project collection that the correct number is used to evaluate the msbuildNodeCount
        /// </summary>
        [TestMethod]
        public void VerifyMsBuildNodeCountReservedProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <abcdef>$(MSBuildNodeCount)</abcdef>
                                </PropertyGroup>
                                
                                <Target Name='t'>
                                    <Message Text='[$(abcdef)]' />
                                </Target>
                              </Project>");

            // Setup a project collection which asks for 4 nodes
            ProjectCollection collection = new ProjectCollection
                              (
                                   ProjectCollection.GlobalProjectCollection.GlobalProperties,
                                   ProjectCollection.GlobalProjectCollection.Loggers,
                                   null,
                                   ProjectCollection.GlobalProjectCollection.ToolsetLocations,
                                   4,
                                   false
                               );

            Project project = new Project(XmlReader.Create(new StringReader(content)), new Dictionary<string, string>(), "4.0", collection);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(String.Format("[{0}]", 4));
        }

        /// <summary>
        /// Verify when no node count is passed that we evaluate MsBuildNodeCount to 1
        /// </summary>
        [TestMethod]
        public void VerifyMsBuildNodeCountReservedPropertyDefault()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <abcdef>$(MSBuildNodeCount)</abcdef>
                                </PropertyGroup>
                                
                                <Target Name='t'>
                                    <Message Text='[$(abcdef)]' />
                                </Target>
                              </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(String.Format("[{0}]", 1));
        }

        /// <summary>
        /// Verify that the programfiles32 property points to the correct location
        /// </summary>
        [TestMethod]
        public void VerifyMsbuildProgramFiles32ReservedProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <abcdef>$(MsBuildProgramFiles32)</abcdef>
                                </PropertyGroup>
                                
                                <Target Name='t'>
                                    <Message Text='[$(abcdef)]' />
                                </Target>
                              </Project>");
            Project project = new Project(XmlReader.Create(new StringReader(content)));

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(String.Format("[{0}]", FrameworkLocationHelper.programFiles32));
        }

        /// <summary>
        /// Basic verification -- adding the tag to the ProjectRootElement on its own does nothing.  
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyTagDoesNothingIfNoGlobalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");
            Project project = new Project(XmlReader.Create(new StringReader(content)));

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains("[Bar]");
        }

        /// <summary>
        /// Basic verification -- with no TreatAsLocalProperty, but with a global property specified, the global property 
        /// overrides the local property.  
        /// </summary>
        [TestMethod]
        public void VerifyGlobalPropertyOverridesIfNoTreatAsLocalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains("[Baz]");
        }

        /// <summary>
        /// Basic verification -- with TreatAsLocalProperty, and with a global property specified, the local property 
        /// overrides the global property.  
        /// </summary>
        [TestMethod]
        public void VerifyLocalPropertyOverridesIfTreatAsLocalPropertySet()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains("[Bar]");
        }

        /// <summary>
        /// Basic verification -- with TreatAsLocalProperty set, but to a different property than is being passed as a global, the 
        /// global property overrides the local property.  
        /// </summary>
        [TestMethod]
        public void VerifyGlobalPropertyOverridesNonSpecifiedLocalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo2"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains("[Baz]");
        }

        /// <summary>
        /// Basic verification -- with TreatAsLocalProperty set, but to a different property than is being passed as a global, the 
        /// global property overrides the local property.  
        /// </summary>
        [TestMethod]
        public void VerifyLocalPropertyInheritsFromOverriddenGlobalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains("[BazBar]");
        }

        /// <summary>
        /// Basic verification -- with TreatAsLocalProperty set, but to a different property than is being passed as a global, the 
        /// global property overrides the local property.  
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertySpecificationWorksIfSpecificationIsItselfAProperty()
        {
            string oldEnvironmentValue = Environment.GetEnvironmentVariable("EnvironmentProperty");

            try
            {
                Environment.SetEnvironmentVariable("EnvironmentProperty", "Bar;Baz");

                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""$(GlobalProperty);$(EnvironmentProperty)"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo2</Foo>
                                    <Bar>$(Bar)Bar2</Bar>
                                    <Baz>$(Baz)Baz2</Baz>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                    <Message Text=' .[[$(Bar)]].' />
                                    <Message Text='.[[[$(Baz)]]].' />
                                </Target>
                              </Project>");

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Foo1");
                globalProperties.Add("Bar", "Bar1");
                globalProperties.Add("Baz", "Baz1");
                globalProperties.Add("GlobalProperty", "Foo");

                Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null, new ProjectCollection());

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains(".[Foo1Foo2].");
                logger.AssertLogContains(".[[Bar1Bar2]].");
                logger.AssertLogContains(".[[[Baz1Baz2]]].");
            }
            finally
            {
                Environment.SetEnvironmentVariable("EnvironmentProperty", oldEnvironmentValue);
            }
        }

        /// <summary>
        /// Basic verification -- setting an invalid TreatAsLocalProperty should be an evaluation error.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InvalidProjectFileException))]
        public void VerifyInvalidTreatAsLocalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""||Bar;Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);

            // Should not reach this point. 
            Assert.Fail();
        }

        /// <summary>
        /// Basic verification -- whitespace in the TreatAsLocalProperty definition should be trimmed.
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyTrimmed()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""   Foo;
                                                                                     Goo
                                "" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo</Foo>
                                    <Bar>$(Goo)Goo</Bar>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text=' .[$(Foo)].' />
                                    <Message Text='.[[$(Bar)]].' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");
            globalProperties.Add("Goo", "Foo");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(".[BazFoo].");
            logger.AssertLogContains(".[[FooGoo]].");
        }

        /// <summary>
        /// Basic verification -- if there are empty entries in the split of the properties for TreatAsLocalProperty, 
        /// they should be ignored. 
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyEmptySplits()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo;;;Goo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Goo</Foo>
                                    <Goo>$(Goo)Goo</Goo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text=' .[$(Foo)].' />
                                    <Message Text='.[[$(Goo)]].' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");
            globalProperties.Add("Goo", "Foo");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(".[BazGoo].");
            logger.AssertLogContains(".[[FooGoo]].");
        }

        /// <summary>
        /// Basic verification -- if looking at the project in the OM, verify that while looking at the property 
        /// value returns the mutable version, looking explicitly at the global properties dictionary still returns 
        /// the original global property value.
        /// </summary>
        [TestMethod]
        public void VerifyGlobalPropertyRetainsOriginalValue()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text=' .[$(Foo)].' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            Assert.AreEqual("BazBar", project.GetPropertyValue("Foo"));
            Assert.AreEqual("Baz", project.GlobalProperties["Foo"]);
        }

        /// <summary>
        /// Basic verification -- if TreatAsLocalProperty is modified on the project XML and then the project is 
        /// re-evaluated, it should be re-evaluated in the context of that modified value. 
        /// </summary>
        [TestMethod]
        public void VerifyModificationsToTreatAsLocalPropertyRespected()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo;Goo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Goo</Foo>
                                    <Goo>$(Goo)Goo</Goo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text=' .[$(Foo)].' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Baz");
            globalProperties.Add("Goo", "Foo");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            Assert.AreEqual("Foo;Goo", project.Xml.TreatAsLocalProperty);

            Assert.AreEqual("BazGoo", project.GetPropertyValue("Foo"));
            Assert.AreEqual("Baz", project.GlobalProperties["Foo"]);
            Assert.AreEqual("FooGoo", project.GetPropertyValue("Goo"));
            Assert.AreEqual("Foo", project.GlobalProperties["Goo"]);

            project.Xml.TreatAsLocalProperty = "Foo";
            project.ReevaluateIfNecessary();

            Assert.AreEqual("BazGoo", project.GetPropertyValue("Foo"));
            Assert.AreEqual("Baz", project.GlobalProperties["Foo"]);
            Assert.AreEqual("Foo", project.GetPropertyValue("Goo"));
            Assert.AreEqual("Foo", project.GlobalProperties["Goo"]);
        }

        /// <summary>
        /// Basic verification -- if TreatAsLocalProperty is modified on the project XML and then the project is 
        /// re-evaluated, it should be re-evaluated in the context of that modified value. 
        /// </summary>
        [TestMethod]
        public void VerifyModificationsToGlobalPropertiesRespected()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text=' .[$(Foo)].' />
                                </Target>
                              </Project>");

            Project project = new Project(XmlReader.Create(new StringReader(content)));

            Assert.AreEqual("Bar", project.GetPropertyValue("Foo"));
            Assert.IsFalse(project.GlobalProperties.ContainsKey("Foo"));

            project.SetGlobalProperty("Foo", "Baz");
            project.ReevaluateIfNecessary();

            Assert.AreEqual("BazBar", project.GetPropertyValue("Foo"));
            Assert.AreEqual("Baz", project.GlobalProperties["Foo"]);

            project.RemoveGlobalProperty("Foo");
            project.ReevaluateIfNecessary();

            Assert.AreEqual("Bar", project.GetPropertyValue("Foo"));
            Assert.IsFalse(project.GlobalProperties.ContainsKey("Foo"));
        }

        /// <summary>
        /// Basic verification -- with TreatAsLocalProperty set to multiple global properties, and with multiple global properties 
        /// passed in, only the ones that are marked TALP are overridable.  
        /// </summary>
        [TestMethod]
        public void VerifyOnlySpecifiedPropertiesOverridden()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo;Bar"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Foo2</Foo>
                                    <Bar>Bar2</Bar>
                                    <Baz>Baz2</Baz>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                    <Message Text=' .[[$(Bar)]].' />
                                    <Message Text='.[[[$(Baz)]]].' />
                                </Target>
                              </Project>");

            IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            globalProperties.Add("Foo", "Foo1");
            globalProperties.Add("Bar", "Bar1");
            globalProperties.Add("Baz", "Baz1");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, null);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);
            logger.AssertLogContains(".[Foo2].");
            logger.AssertLogContains(".[[Bar2]].");
            logger.AssertLogContains(".[[[Baz1]]].");
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in a parent project, that property is still treated as overridable 
        /// when defined in an imported project. 
        /// </summary>
        [TestMethod]
        public void VerifyPropertySetInImportStillOverrides()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>

                                <Import Project=""import.proj"" />
                              </Project>");

            string importContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Bar</Foo>
                                </PropertyGroup>
                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyPropertySetInImportStillOverrides");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string import = Path.Combine(projectDirectory, "import.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(import, importContents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Baz");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains("[BazBar]");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in an imported project, any instances of that property in the parent 
        /// project before the import are ignored and the global property value is used instead. 
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyInImportDoesntAffectParentProjectAboveIt()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>

                                <Import Project=""import.proj"" />
                              </Project>");

            string importContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>

                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyTreatAsLocalPropertyInImportDoesntAffectParentProjectAboveIt");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string import = Path.Combine(projectDirectory, "import.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(import, importContents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Baz");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains("[Baz]");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in an imported project, any instances of that property in the parent 
        /// project after the import recognize the TreatAsLocalProperty flag and override the global property value. 
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyInImportAffectsParentProjectBelowIt()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Import Project=""import.proj"" />
                                <PropertyGroup>
                                    <Foo>Bar</Foo>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='[$(Foo)]' />
                                </Target>
                              </Project>");

            string importContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>

                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyTreatAsLocalPropertyInImportAffectsParentProjectBelowIt");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string import = Path.Combine(projectDirectory, "import.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(import, importContents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Baz");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains("[Bar]");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in both the parent and imported project, the end result 
        /// set of overridable properties is the union of the two sets, though of course you cannot 
        /// override a property until you reach the import that mentions it in its TreatAsLocalProperty 
        /// parameter.
        /// </summary>
        [TestMethod]
        public void VerifyTreatAsLocalPropertyUnionBetweenImports()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo1</Foo>
                                    <Bar>$(Bar)Bar1</Bar>
                                    <Baz>$(Baz)Baz1</Baz>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                    <Message Text=' .[[$(Bar)]].' />
                                    <Message Text='.[[[$(Baz)]]].' />
                                </Target>

                                <Import Project=""import.proj"" />
                            </Project>");

            string importContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Bar;Baz"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo2</Foo>
                                    <Bar>$(Bar)Bar2</Bar>
                                    <Baz>$(Baz)Baz2</Baz>
                                </PropertyGroup>

                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyTreatAsLocalPropertyUnionBetweenImports");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string import = Path.Combine(projectDirectory, "import.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(import, importContents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Foo3");
                globalProperties.Add("Bar", "Bar3");
                globalProperties.Add("Baz", "Baz3");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains(".[Foo3Foo1Foo2].");
                logger.AssertLogContains(".[[Bar3Bar2]].");
                logger.AssertLogContains(".[[[Baz3Baz2]]].");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If a property is set to TreatAsLocalProperty in both the parent project and the import, this is 
        /// silently acknowledged.
        /// </summary>
        [TestMethod]
        public void VerifyDuplicateTreatAsLocalProperty()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo1</Foo>
                                    <Bar>$(Bar)Bar1</Bar>
                                    <Baz>$(Baz)Baz1</Baz>
                                </PropertyGroup>

                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                    <Message Text=' .[[$(Bar)]].' />
                                    <Message Text='.[[[$(Baz)]]].' />
                                </Target>
                                <Import Project=""import.proj"" />
                              </Project>");

            string importContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo;Bar;Baz"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo2</Foo>
                                    <Bar>$(Bar)Bar2</Bar>
                                    <Baz>$(Baz)Baz2</Baz>
                                </PropertyGroup>

                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyDuplicateTreatAsLocalProperty");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string import = Path.Combine(projectDirectory, "import.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(import, importContents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Foo3");
                globalProperties.Add("Bar", "Bar3");
                globalProperties.Add("Baz", "Baz3");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains(".[Foo3Foo1Foo2].");
                logger.AssertLogContains(".[[Bar3Bar2]].");
                logger.AssertLogContains(".[[[Baz3Baz2]]].");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in a parent project, a project that is P2P'ed to will 
        /// still receive the original value of the global property. 
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void VerifyGlobalPropertyPassedToP2P()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo2</Foo>
                                </PropertyGroup>

                                <Target Name='t0'>
                                       <MSBuild Projects=""project2.proj"" Targets=""t"" />
                                </Target>
                              </Project>");

            string project2Contents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                </Target>
                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyGlobalPropertyPassedToP2P");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string project2 = Path.Combine(projectDirectory, "project2.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(project2, project2Contents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Foo1");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains(".[Foo1].");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// If TreatAsLocalProperty is set in a parent project, a project that is P2P'ed who is explicitly 
        /// passed the property, will get the mutable local value rather than the original value of the 
        /// global property.
        /// </summary>
        [TestMethod]
        [Ignore]
        // Ignore: Changes to the current directory interfere with the toolset reader.
        public void VerifyLocalPropertyPropagatesIfExplicitlyPassedToP2P()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" TreatAsLocalProperty=""Foo"" xmlns='msbuildnamespace'>
                                <PropertyGroup>
                                    <Foo>$(Foo)Foo2</Foo>
                                </PropertyGroup>

                                <Target Name='t0'>
                                       <MSBuild Projects=""project2.proj"" Targets=""t"" Properties=""Foo=$(Foo)"" />
                                </Target>
                              </Project>");

            string project2Contents = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='  .[$(Foo)].' />
                                </Target>
                              </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyLocalPropertyPropagatesIfExplicitlyPassedToP2P");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string primaryProject = Path.Combine(projectDirectory, "project.proj");
                string project2 = Path.Combine(projectDirectory, "project2.proj");

                File.WriteAllText(primaryProject, projectContents);
                File.WriteAllText(project2, project2Contents);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                globalProperties.Add("Foo", "Foo1");

                Project project = new Project(primaryProject, globalProperties, null);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);
                logger.AssertLogContains(".[Foo1Foo2].");
            }
            finally
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }
            }
        }

        /// <summary>
        /// Verify that when we don't specify the sub-toolset version, we get the correct sub-toolset properties
        /// based on the default sub-toolset version -- base toolset if Dev10 is installed, or lowest (numerically
        /// sorted) toolset if it's not.  
        /// </summary>
        [TestMethod]
        public void VerifyDefaultSubToolsetPropertiesAreEvaluated()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");
            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", null);
                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no global properties */);
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                if (Toolset.Dev10IsInstalled)
                {
                    // if Dev10 is installed, the default sub-toolset is nothing == base toolset.
                    logger.AssertLogContains(".[a1].");
                    logger.AssertLogContains(".[[b1]].");
                    logger.AssertLogContains(".[[[]]].");
                    logger.AssertLogContains(".[[[[10.0]]]].");
                }
                else
                {
                    // if Dev10 is not installed, the default sub-toolset is the numerical least -- in our case, "11.0" -- 
                    // so the toolset properties are a combination of that + the base toolset. 
                    logger.AssertLogContains(".[a1].");
                    logger.AssertLogContains(".[[b2]].");
                    logger.AssertLogContains(".[[[c2]]].");
                    logger.AssertLogContains(".[[[[11.0]]]].");
                }

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a1||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that when we specify an invalid sub-toolset version, we just get the properties from the base 
        /// toolset ... but that invalid version is still reflected as a project property.  
        /// </summary>
        [TestMethod]
        public void VerifyNoSubToolsetPropertiesAreEvaluatedWithInvalidSubToolset()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");
            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "ABCDE");
                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no global properties */);
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a1].");
                logger.AssertLogContains(".[[b1]].");
                logger.AssertLogContains(".[[[]]].");
                logger.AssertLogContains(".[[[[ABCDE]]]].");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a1||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that if a sub-toolset is explicitly specified, its properties are evaluated into the project properly.
        /// </summary>
        [TestMethod]
        public void VerifyExplicitSubToolsetPropertiesAreEvaluated()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");
            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "FakeSubToolset");
                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no global properties */);
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a3].");
                logger.AssertLogContains(".[[b1]].");
                logger.AssertLogContains(".[[[c3]]].");
                logger.AssertLogContains(".[[[[FakeSubToolset]]]].");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a3||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that if a non-existent sub-toolset is specified, we simply ignore it and just use the base toolset properties. 
        /// </summary>
        [TestMethod]
        public void VerifyExplicitNonExistentSubToolsetPropertiesAreEvaluated()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");
            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "abcdef");
                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no global properties */);
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a1].");
                logger.AssertLogContains(".[[b1]].");
                logger.AssertLogContains(".[[[]]].");
                logger.AssertLogContains(".[[[[abcdef]]]].");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a1||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that if there is a conflict between sub-toolset and environment properties, the sub-toolset properties win.  
        /// </summary>
        [TestMethod]
        public void VerifySubToolsetPropertiesOverrideEnvironment()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");
            string originalC = Environment.GetEnvironmentVariable("C");
            string originalD = Environment.GetEnvironmentVariable("D");

            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "FakeSubToolset");
                Environment.SetEnvironmentVariable("C", "c4"); // not explosive :) 
                Environment.SetEnvironmentVariable("D", "d4");

                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='    .[$(a)].' />
                                    <Message Text='   .[[$(b)]].' />
                                    <Message Text='  .[[[$(c)]]].' />
                                    <Message Text=' .[[[[$(d)]]]].' />
                                    <Message Text='.[[[[[$(VisualStudioVersion)]]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no global properties */);
                Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a3].");
                logger.AssertLogContains(".[[b1]].");
                logger.AssertLogContains(".[[[c3]]].");
                logger.AssertLogContains(".[[[[d4]]]].");
                logger.AssertLogContains(".[[[[[FakeSubToolset]]]]]");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a3||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
                Environment.SetEnvironmentVariable("C", originalC);
                Environment.SetEnvironmentVariable("D", originalD);
            }
        }

        /// <summary>
        /// Verify that if there is a conflict between sub-toolset and global properties, the global properties win.  
        /// </summary>
        [TestMethod]
        public void VerifyGlobalPropertiesOverrideSubToolset()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");

            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "FakeSubToolset");

                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='    .[$(a)].' />
                                    <Message Text='   .[[$(b)]].' />
                                    <Message Text='  .[[[$(c)]]].' />
                                    <Message Text=' .[[[[$(d)]]]].' />
                                    <Message Text='.[[[[[$(VisualStudioVersion)]]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no project collection global properties */);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties.Add("c", "c5");
                globalProperties.Add("d", "d5");

                Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a3].");
                logger.AssertLogContains(".[[b1]].");
                logger.AssertLogContains(".[[[c5]]].");
                logger.AssertLogContains(".[[[[d5]]]].");
                logger.AssertLogContains(".[[[[[FakeSubToolset]]]]].");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a3||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that even if the sub-toolset was set by a global property, it can be overridden from within the project
        /// </summary>
        [TestMethod]
        public void VerifySubToolsetVersionSetByGlobalPropertyStillOverridable()
        {
            string originalVisualStudioVersion = Environment.GetEnvironmentVariable("VisualStudioVerson");

            try
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", "FakeSubToolset");

                string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

                ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no project collection global properties */);

                IDictionary<string, string> globalProperties = new Dictionary<string, string>();
                globalProperties.Add("VisualStudioVersion", "11.0");

                Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, "Fake", fakeProjectCollection);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
                Assert.AreEqual(true, result);

                logger.AssertLogContains(".[a1].");
                logger.AssertLogContains(".[[b2]].");
                logger.AssertLogContains(".[[[c2]]].");
                logger.AssertLogContains(".[[[[11.0]]]].");

                // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
                // the value of any of the sub-toolset properties. 
                logger.AssertLogContains("|changed|");
                logger.AssertLogContains("||a1||");
            }
            finally
            {
                Environment.SetEnvironmentVariable("VisualStudioVersion", originalVisualStudioVersion);
            }
        }

        /// <summary>
        /// Verify that if the sub-toolset was set by a global property, it cannot be overridden from within the project
        /// </summary>
        [TestMethod]
        public void VerifySubToolsetVersionSetByConstructorOverridable_OverridesGlobalProperty()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

            ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no project collection global properties */);

            IDictionary<string, string> globalProperties = new Dictionary<string, string>();
            globalProperties.Add("VisualStudioVersion", "11.0");

            Project project = new Project(XmlReader.Create(new StringReader(content)), globalProperties, "Fake", "FakeSubToolset", fakeProjectCollection, ProjectLoadSettings.Default);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);

            logger.AssertLogContains(".[a3].");
            logger.AssertLogContains(".[[b1]].");
            logger.AssertLogContains(".[[[c3]]].");
            logger.AssertLogContains(".[[[[FakeSubToolset]]]].");

            // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
            // the value of any of the sub-toolset properties. 
            logger.AssertLogContains("|changed|");
            logger.AssertLogContains("||a3||");
        }

        /// <summary>
        /// Verify that if the sub-toolset was set by a global property, it cannot be overridden from within the project
        /// </summary>
        [TestMethod]
        public void VerifySubToolsetVersionSetByConstructorOverridable()
        {
            string content = ObjectModelHelpers.CleanupFileContents(@"
                             <Project ToolsVersion=""msbuilddefaulttoolsversion"" xmlns='msbuildnamespace'>
                                <Target Name='t'>
                                    <Message Text='   .[$(a)].' />
                                    <Message Text='  .[[$(b)]].' />
                                    <Message Text=' .[[[$(c)]]].' />
                                    <Message Text='.[[[[$(VisualStudioVersion)]]]].' />
                                </Target>
                                <Target Name='t2' AfterTargets='t'>
                                    <PropertyGroup>
                                        <VisualStudioVersion>changed</VisualStudioVersion>
                                    </PropertyGroup>

                                    <Message Text='|$(VisualStudioVersion)|' />
                                    <Message Text='||$(a)||' />
                                </Target>
                              </Project>");

            ProjectCollection fakeProjectCollection = GetProjectCollectionWithFakeToolset(null /* no project collection global properties */);

            Project project = new Project(XmlReader.Create(new StringReader(content)), null, "Fake", "FakeSubToolset", fakeProjectCollection, ProjectLoadSettings.Default);

            MockLogger logger = new MockLogger();
            bool result = project.Build(logger);
            Assert.AreEqual(true, result);

            logger.AssertLogContains(".[a3].");
            logger.AssertLogContains(".[[b1]].");
            logger.AssertLogContains(".[[[c3]]].");
            logger.AssertLogContains(".[[[[FakeSubToolset]]]].");

            // whatever the initial value of VisualStudioVersion, we should be able to change it, but it doesn't affect
            // the value of any of the sub-toolset properties. 
            logger.AssertLogContains("|changed|");
            logger.AssertLogContains("||a3||");
        }

        /// <summary>
        /// Verify that DTD processing is disabled when loading a project
        /// We add some invalid DTD code to a MSBuild project, if such code is ever parsed a XmlException will be thrown
        /// If DTD parsing is desabled (desired behavior), no XmlException should be caught
        /// </summary>
        [TestMethod]
        public void VerifyDTDProcessingIsDisabled()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(
                                @"<?xml version=""1.0"" encoding=""utf-8""?>
                                <!DOCTYPE Project [
                                <!ELEMENT DUMMYELEMENT (SOMETHING+)>
                                <INVALID_DTD>
                                ]>
                                <Project ToolsVersion=""msbuilddefaulttoolsversion"" DefaultTargets=""SimpleMessage"" xmlns=""msbuildnamespace"">
                                    <Target Name=""SimpleMessage"">
                                        <Message Text=""Dummy project""/>
                                    </Target>
                                </Project>");
            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyDTDProcessingIsDisabled");

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string projectFilename = Path.Combine(projectDirectory, "project.proj");

                File.WriteAllText(projectFilename, projectContents);

                Project project = new Project(projectFilename);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
            }
            catch (XmlException)
            {
                // XmlException thrown when invalid DTD statement is parsed: it means DTD processing was enabled
                Assert.IsTrue(false);
            }
        }

        /// <summary>
        /// Verify that DTD processing is disabled when loading a project
        /// We create an HTTP server that waits for a request and load a project containing DTD code making reference to a ficticious file in the server.
        /// This test emulates a scenario where some malicious DTD code could upload user data to a malicious website
        /// If DTD processing is disabled, the server should not receive any connection request.
        /// </summary>
        [TestMethod]
        public void VerifyDTDProcessingIsDisabled2()
        {
            string projectContents = ObjectModelHelpers.CleanupFileContents(@"<?xml version=""1.0"" encoding=""utf-8""?>
                                <!DOCTYPE Project [
                                    <!ENTITY % external SYSTEM ""http://localhost:51111/ext.xml"">
                                    %external;
                                    %param1;
                                    %test;
                                ]>
                                <Project ToolsVersion=""msbuilddefaulttoolsversion"" DefaultTargets=""SimpleMessage"" xmlns=""msbuildnamespace"">
                                    <Target Name=""SimpleMessage"">
                                        <Message Text=""Dummy project""/>
                                    </Target>
                                </Project>");

            string projectDirectory = Path.Combine(Path.GetTempPath(), "VerifyDTDProcessingIsDisabled");

            Thread t = new Thread(HttpServerThread);
            t.IsBackground = true;
            t.Start();

            try
            {
                if (Directory.Exists(projectDirectory))
                {
                    Directory.Delete(projectDirectory, true /* recursive delete */);
                }

                Directory.CreateDirectory(projectDirectory);

                string projectFilename = Path.Combine(projectDirectory, "project.proj");

                File.WriteAllText(projectFilename, projectContents);

                Project project = new Project(projectFilename);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);
            }
            finally
            {
                Thread.Sleep(500);

                // Expect server to be alive and hung up unless a request originating from DTD processing was sent
                Assert.IsTrue(t.IsAlive);
            }
        }

        /// <summary>
        /// Verify that Condition Evaluator does reset the cached state when the evaluation throws an exception.
        /// </summary>
        [TestMethod]
        public void VerifyConditionEvaluatorResetStateOnFailure()
        {
            PropertyDictionary<ProjectPropertyInstance> propertyBag = new PropertyDictionary<ProjectPropertyInstance>();
            Expander<ProjectPropertyInstance, ProjectItemInstance> expander = new Expander<ProjectPropertyInstance, ProjectItemInstance>(propertyBag);
            string condition = " '$(TargetOSFamily)' >= '3' ";

            // Give an incorrect value for the property "TargetOSFamily", and then the evaluation should throw an exception.
            propertyBag.Set(ProjectPropertyInstance.Create("TargetOSFamily", "*"));
            try
            {
                ConditionEvaluator.EvaluateCondition(
                    condition,
                    ParserOptions.AllowAll,
                    expander,
                    ExpanderOptions.ExpandProperties,
                    Environment.CurrentDirectory,
                    MockElementLocation.Instance,
                    null,
                    new DefaultLicenseValidator(1, 2, 3, 4));
                Assert.Fail("Expect exception due to the value of property \"TargetOSFamily\" is not a number.");
            }
            catch (InvalidProjectFileException e)
            {
                Console.WriteLine("Expect exception: " + e.Message);
            }

            // Correct the property "TargetOSFamily", and then the evaluation should succeed.
            propertyBag.Set(ProjectPropertyInstance.Create("TargetOSFamily", "3"));
            Assert.IsTrue(ConditionEvaluator.EvaluateCondition(
                condition,
                ParserOptions.AllowAll,
                expander,
                ExpanderOptions.ExpandProperties,
                Environment.CurrentDirectory,
                MockElementLocation.Instance,
                null,
                new DefaultLicenseValidator(1, 2, 3, 4)));
        }

        /// <summary>
        /// HTTP server code running on a separate thread that expects a connection request
        /// The test "VerifyDTDProcessingIsDisabled" creates a project with a url reference to this server from a DTD tag
        /// If a connection request is received, this thread will terminate, if not, the server will remain alive until 
        /// "VerifyDTDProcessingIsDisabled" returns.
        /// </summary>
        static private void HttpServerThread()
        {
            HttpListener listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:51111/");
            listener.Start();

            HttpListenerContext context = listener.GetContext();

            // if reached this point it means the server answered a request triggered during DTD processing
            listener.Stop();
        }

        /// <summary>
        /// Creates a standard ProjectCollection and adds a fake toolset with the following contents to it:  
        /// 
        /// ToolsVersion = Fake
        /// Base Properties: 
        /// a = a1
        /// b = b1
        /// 
        /// SubToolset "11.0": 
        /// b = b2
        /// c = c2
        /// 
        /// SubToolset "FakeSubToolset":
        /// a = a3
        /// c = c3
        /// </summary>
        private ProjectCollection GetProjectCollectionWithFakeToolset(IDictionary<string, string> globalProperties)
        {
            ProjectCollection projectCollection = new ProjectCollection(globalProperties);

            IDictionary<string, string> properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            properties.Add("a", "a1");
            properties.Add("b", "b1");

            Dictionary<string, SubToolset> subToolsets = new Dictionary<string, SubToolset>(StringComparer.OrdinalIgnoreCase);

            // SubToolset 11.0 properties
            PropertyDictionary<ProjectPropertyInstance> subToolset11Properties = new PropertyDictionary<ProjectPropertyInstance>();
            subToolset11Properties.Set(ProjectPropertyInstance.Create("b", "b2"));
            subToolset11Properties.Set(ProjectPropertyInstance.Create("c", "c2"));

            // FakeSubToolset properties
            PropertyDictionary<ProjectPropertyInstance> fakeSubToolsetProperties = new PropertyDictionary<ProjectPropertyInstance>();
            fakeSubToolsetProperties.Set(ProjectPropertyInstance.Create("a", "a3"));
            fakeSubToolsetProperties.Set(ProjectPropertyInstance.Create("c", "c3"));

            subToolsets.Add("FakeSubToolset", new SubToolset("FakeSubToolset", fakeSubToolsetProperties));
            subToolsets.Add("11.0", new SubToolset("11.0", subToolset11Properties));

            Toolset parentToolset = projectCollection.GetToolset("4.0");

            Toolset fakeToolset = new Toolset("Fake", parentToolset.ToolsPath, properties, projectCollection, subToolsets, parentToolset.OverrideTasksPath);

            projectCollection.AddToolset(fakeToolset);

            return projectCollection;
        }

        /// <summary>
        /// To the target provided add messages to dump all the MSBuildThisFileXXXX properties.
        /// </summary>
        private void AddPropertyDumpTasks(string prefix, ProjectTargetElement target)
        {
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFileDirectory=$(MSBuildThisFileDirectory)");
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFileDirectoryNoRoot=$(MSBuildThisFileDirectoryNoRoot)");
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFile=$(MSBuildThisFile)");
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFileExtension=$(MSBuildThisFileExtension)");
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFileFullPath=$(MSBuildThisFileFullPath)");
            target.AddTask("Message").SetParameter("Text", prefix + ": MSBuildThisFileName=$(MSBuildThisFileName)");
        }

        /// <summary>
        /// Creates a file on disk that logs [$(MSBuildThisFile)]
        /// </summary>
        private void CreateTargetsFileWithMessage(string path, string targetName, string dependsOn)
        {
            ProjectRootElement import = ProjectRootElement.Create(path);
            ProjectTargetElement target = import.AddTarget(targetName);
            target.AddTask("Message").SetParameter("Text", "[$(MSBuildThisFile)]");
            target.DependsOnTargets = dependsOn;
            import.Save();
        }

        /// <summary>
        /// Verifies that the import path.
        /// </summary>
        private void VerifyImportTargetRelativePath(string directory, string directory2, string[] imports)
        {
            string file0 = null;
            string file1 = null;
            string file2 = null;
            string file3 = null;
            string file4 = null;

            try
            {
                if (File.Exists(directory))
                {
                    Directory.Delete(directory);
                }

                file0 = Path.Combine(directory, "my.proj");
                file1 = Path.Combine(directory, "1.targets");
                file2 = Path.Combine(directory2, "2.targets");
                file3 = Path.Combine(directory2, "3.cpp.targets");
                file4 = Path.Combine(directory2, "4.nottargets");

                ProjectRootElement projectXml = ProjectRootElement.Create(file0);
                projectXml.DefaultTargets = "t1";
                foreach (string import in imports)
                {
                    projectXml.AddImport(import);
                }

                CreateTargetsFileWithMessage(file1, "t1", "t3");
                CreateTargetsFileWithMessage(file2, "t2", "");
                CreateTargetsFileWithMessage(file3, "t3", "t2");
                CreateTargetsFileWithMessage(file4, "t4", "t3");

                Project project = new Project(projectXml);

                MockLogger logger = new MockLogger();
                bool result = project.Build(logger);

                Assert.AreEqual(true, result);

                logger.AssertLogContains(new string[] { "[2.targets]", "[3.cpp.targets]", "[1.targets]" });
                logger.AssertLogDoesntContain("4.nottargets");

                logger.ClearLog();

                result = project.Build("t4");

                Assert.AreEqual(false, result);
            }
            finally
            {
                File.Delete(file1);
                File.Delete(file2);
                File.Delete(file3);
                File.Delete(file4);
                Directory.Delete(directory, true);
            }
        }

        #region ProjectPropertyComparer

        /// <summary>
        /// Checks two ProjectProperty objects belonging to the same project for equality.
        /// </summary>
        private class ProjectPropertyComparer : IEqualityComparer<ProjectProperty>
        {
            /// <summary>
            /// Checks if two ProjectProperty objects are semantically equal.
            /// </summary>
            /// <param name="x"> The first object. </param>
            /// <param name="y"> The second object. </param>
            /// <returns> If they are semantically equal. </returns>
            public bool Equals(ProjectProperty x, ProjectProperty y)
            {
                bool areEqual = false;

                if (Object.ReferenceEquals(x, y))
                {
                    // If they point to the same object or are both null, they are equal.
                    areEqual = true;
                }
                else if (x == null ^ y == null)
                {
                    // If only one of them is null, they are NOT equal.
                    areEqual = false;
                }
                else if (!Object.ReferenceEquals(x.Project, y.Project))
                {
                    // If they don't belong to the same project, they are not equal.
                    areEqual = false;
                }
                else if (x.Xml != null && y.Xml != null && Object.ReferenceEquals(x.Xml, y.Xml))
                {
                    // If their underlying construction model elements are the same, they are equal.
                    // Note that certain properties such as global/environment/toolset properties
                    // do not have a backing xml.
                    areEqual = true;
                }
                else if (x.Xml == null && y.Xml == null) // both are global/environment/toolset properties
                {
                    // If both their unevaluated values as well as their evaluated values are same, then they are equal.
                    areEqual = String.Equals(x.UnevaluatedValue, y.UnevaluatedValue, StringComparison.OrdinalIgnoreCase);
                }

                return areEqual;
            }

            /// <summary>
            /// Returns the hash code for a ProjectProperty object. 
            /// </summary>
            /// <param name="obj"> A ProjectProperty object. </param>
            /// <returns> The has code. </returns>
            public int GetHashCode(ProjectProperty obj)
            {
                int hashCode = StringComparer.OrdinalIgnoreCase.GetHashCode(obj.UnevaluatedValue);

                return hashCode;
            }
        }

        #endregion
    }
}
