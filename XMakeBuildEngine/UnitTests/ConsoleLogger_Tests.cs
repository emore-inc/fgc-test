﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;
using System.IO;
using System.Reflection;
using System.Collections;
using System.Globalization;
using System.Collections.Generic;

using Microsoft.VisualStudio.TestTools.UnitTesting;

using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Shared;

using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;

namespace Microsoft.Build.UnitTests
{
    [TestClass]
    public class ConsoleLoggerTest
    {
        /// <summary>
        /// For the environment writing test
        /// </summary>
        private Dictionary<string, string> _environment;

        private static string s_dummyProjectContents = @"
         <Project xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
            <Target Name='XXX'>
               <Message Text='[hee haw]'/>
            </Target>
            <Target Name='YYY' AfterTargets='XXX'>
            </Target>
            <Target Name='GGG' AfterTargets='XXX'>
               <Exec Command='where.exe where'/>
            </Target>
         </Project>";


        private class SimulatedConsole
        {
            private StringBuilder _simulatedConsole;

            internal SimulatedConsole()
            {
                _simulatedConsole = new StringBuilder();
            }

            internal void Clear()
            {
                _simulatedConsole = new StringBuilder();
            }

            public override string ToString()
            {
                return _simulatedConsole.ToString();
            }

            internal void Write(string s)
            {
                _simulatedConsole.Append(s);
            }

            internal void WriteLine(string s)
            {
                Write(s);
                Write(Environment.NewLine);
            }

            internal void SetColor(ConsoleColor c)
            {
                switch (c)
                {
                    case ConsoleColor.Red:
                        _simulatedConsole.Append("<red>");
                        break;

                    case ConsoleColor.Yellow:
                        _simulatedConsole.Append("<yellow>");
                        break;

                    case ConsoleColor.Cyan:
                        _simulatedConsole.Append("<cyan>");
                        break;

                    case ConsoleColor.DarkGray:
                        _simulatedConsole.Append("<darkgray>");
                        break;

                    case ConsoleColor.Green:
                        _simulatedConsole.Append("<green>");
                        break;

                    default:
                        _simulatedConsole.Append("<ERROR: invalid color>");
                        break;
                }
            }

            internal void ResetColor()
            {
                _simulatedConsole.Append("<reset color>");
            }

            public static implicit operator string (SimulatedConsole sc)
            {
                return sc.ToString();
            }
        }

        private static void SingleMessageTest(LoggerVerbosity v, MessageImportance j, bool shouldPrint)
        {
            for (int i = 1; i <= 2; i++)
            {
                SimulatedConsole sc = new SimulatedConsole();
                EventSourceSink es = new EventSourceSink();
                ConsoleLogger L = new ConsoleLogger(v,
                                  sc.Write, null, null);
                L.Initialize(es, i);
                string msg = "my 1337 message";

                BuildMessageEventArgs be = new BuildMessageEventArgs(msg, "help", "sender", j);
                be.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
                es.Consume(be);

                if (i == 2 && v == LoggerVerbosity.Diagnostic)
                {
                    string context = ResourceUtilities.FormatResourceString("DefaultLicenseValidator", LogFormatter.FormatLogTimeStamp(be.Timestamp), 0) + ">";
                    msg = context + ResourceUtilities.FormatResourceString("TaskMessageWithId", "my 1337 message", be.DefaultLicenseValidator.TaskId);
                }
                else if (i == 2 && v == LoggerVerbosity.Detailed)
                {
                    string context = ResourceUtilities.FormatResourceString("DefaultLicenseValidator", string.Empty, 0) + ">";
                    msg = context + "my 1337 message";
                }
                else if (i == 2)
                {
                    msg = "  " + msg;
                }

                Assert.AreEqual(shouldPrint ? msg + Environment.NewLine : String.Empty, sc.ToString());
            }
        }

        private sealed class MyCustomCalcArrayWrappingScalar : CustomCalcArrayWrappingScalar
        {
            internal MyCustomCalcArrayWrappingScalar()
                : base()
            {
                // do nothing
            }

            internal MyCustomCalcArrayWrappingScalar(string message)
                : base(message, null, null)
            {
                // do nothing
            }
        }

        private class MyCustomCalcArrayWrappingScalar2 : CustomCalcArrayWrappingScalar { }

        [TestInitialize]
        public void SuiteSetup()
        {
            _environment = new Dictionary<string, string>();

            foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
            {
                _environment.Add((string)entry.Key, (string)entry.Value);
            }
        }


        /// <summary>
        /// Verify when the project has not been named that we correctly get the same placeholder
        /// project name for for project started event and the target started event. 
        /// Test for BUG 579935
        /// </summary>
        [TestMethod]
        public void TestEmptyProjectNameForTargetStarted()
        {
            Microsoft.Build.Evaluation.Project project = new Microsoft.Build.Evaluation.Project();

            ProjectTargetElement target = project.Xml.AddTarget("T");
            ProjectTaskElement task = target.AddTask("Message");

            System.Xml.XmlAttribute attribute = task.XmlDocument.CreateAttribute("Text");
            attribute.Value = "HELLO";

            attribute = task.XmlDocument.CreateAttribute("MessageImportance");
            attribute.Value = "High";

            MockLogger mockLogger = new MockLogger();
            List<ILogger> loggerList = new List<ILogger>();
            loggerList.Add(mockLogger);
            project.Build(loggerList);

            List<ProjectStartedEventArgs> projectStartedEvents = mockLogger.ProjectStartedEvents;
            Assert.IsTrue(projectStartedEvents.Count == 1);
            string projectStartedName = projectStartedEvents[0].ProjectFile;
            Assert.IsFalse(String.IsNullOrEmpty(projectStartedName), "Expected project started name to not be null or empty");

            List<TargetStartedEventArgs> targetStartedEvents = mockLogger.TargetStartedEvents;
            Assert.IsTrue(targetStartedEvents.Count == 1);
            Assert.IsTrue(projectStartedName.Equals(targetStartedEvents[0].ProjectFile, StringComparison.OrdinalIgnoreCase), "Expected the project started and target started target names to match");
        }


        /// <summary>
        /// Make sure the first message after a project started event prints out the target name. This was annoying a lot of people when there were messages right after the project
        /// started event but there was no target printed out.
        /// </summary>
        [TestMethod]
        public void TestTargetAfterProjectStarted()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            string log = sc.ToString();
            Assert.IsTrue(log.IndexOf("XXX:", StringComparison.OrdinalIgnoreCase) != -1);
        }

        /// <summary>
        /// Verify that on minimal verbosity the console logger does not log the target names.
        /// </summary>
        [TestMethod]
        public void TestNoTargetNameOnMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            string log = sc.ToString();
            Assert.IsTrue(log.IndexOf("XXX:", StringComparison.OrdinalIgnoreCase) == -1);
            Assert.IsTrue(log.IndexOf("YYY:", StringComparison.OrdinalIgnoreCase) == -1);
            Assert.IsTrue(log.IndexOf("GGG:", StringComparison.OrdinalIgnoreCase) == -1);
        }

        /// <summary>
        /// Make sure if a target has no messages logged that its started and finished events show up on detailed but not normal.
        /// </summary>
        [TestMethod]
        public void EmptyTargetsOnDetailedButNotNotmal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            string log = sc.ToString();
            Assert.IsTrue(log.IndexOf("YYY:", StringComparison.OrdinalIgnoreCase) == -1);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            string tempProjectDir = Path.Combine(Path.GetTempPath(), "EmptyTargetsOnDetailedButNotNotmal");
            string tempProjectPath = Path.Combine(tempProjectDir, "test.proj");

            try
            {
                if (FileUtilities.DirectoryExistsNoThrow(tempProjectDir))
                {
                    FileUtilities.DeleteDirectoryNoThrow(tempProjectDir, true);
                }

                Directory.CreateDirectory(tempProjectDir);
                File.WriteAllText(tempProjectPath, s_dummyProjectContents);

                ObjectModelHelpers.BuildTempProjectFileWithTargets(tempProjectPath, null, null, logger);

                log = sc.ToString();
                string targetStartedMessage = ResourceUtilities.FormatResourceString("TargetStartedProjectEntry", "YYY", tempProjectPath);

                // it's a console, so it cuts off, so only look for the existence of the first bit (which should contains the "YYY")
                targetStartedMessage = targetStartedMessage.Substring(0, 60);
                Assert.IsTrue(log.IndexOf(targetStartedMessage, StringComparison.OrdinalIgnoreCase) != -1);
            }
            finally
            {
                if (FileUtilities.DirectoryExistsNoThrow(tempProjectDir))
                {
                    FileUtilities.DeleteDirectoryNoThrow(tempProjectDir, true);
                }
            }
        }

        /// <summary>
        /// Test a number of cases where difference values from showcommandline are used with normal verbosity
        /// </summary>
        [TestMethod]
        public void ShowCommandLineWithNormalVerbosity()
        {
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            string log = sc.ToString();
            Assert.IsTrue(log.IndexOf("where.exe where", StringComparison.OrdinalIgnoreCase) != -1);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=true";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            log = sc.ToString();
            Assert.IsTrue(log.IndexOf("where.exe where", StringComparison.OrdinalIgnoreCase) != -1);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=false";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            log = sc.ToString();
            Assert.IsTrue(log.IndexOf("where.exe where", StringComparison.OrdinalIgnoreCase) == -1);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging;ShowCommandLine=NotAbool";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);
            log = sc.ToString();
            Assert.IsTrue(log.IndexOf("where.exe where", StringComparison.OrdinalIgnoreCase) == -1);

            sc = new SimulatedConsole();
            logger = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);
            logger.Parameters = "EnableMPLogging";
            ObjectModelHelpers.BuildProjectExpectSuccess(s_dummyProjectContents, logger);

            log = sc.ToString();
            Assert.IsTrue(log.IndexOf("where.exe where", StringComparison.OrdinalIgnoreCase) != -1);
        }

        /// <summary>
        /// We should not crash when given a null message, etc.
        /// </summary>
        [TestMethod]
        public void NullEventFields()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es);

            // Not all parameters are null here, but that's fine, we assume the engine will never
            // fire a ProjectStarted without a project name, etc.
            es.Consume(new BuildStartedEventArgs(null, null));
            es.Consume(new ProjectStartedEventArgs(null, null, "p", null, null, null));
            es.Consume(new TargetStartedEventArgs(null, null, "t", null, null));
            es.Consume(new TaskStartedEventArgs(null, null, null, null, "task"));
            es.Consume(new BuildMessageEventArgs(null, null, null, MessageImportance.High));
            es.Consume(new BuildWarningEventArgs(null, null, null, 0, 0, 0, 0, null, null, null));
            es.Consume(new DialogWindowEditorToStringValueConverter(null, null, null, 0, 0, 0, 0, null, null, null));
            es.Consume(new TaskFinishedEventArgs(null, null, null, null, "task", true));
            es.Consume(new TargetFinishedEventArgs(null, null, "t", null, null, true));
            es.Consume(new ProjectFinishedEventArgs(null, null, "p", true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new BuildFinishedEventArgs(null, null, true));
            es.Consume(new MyCustomCalcArrayWrappingScalar2());
            // No exception raised
        }

        [TestMethod]
        public void NullEventFieldsParallel()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es, 2);
            DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

            BuildStartedEventArgs bse = new BuildStartedEventArgs(null, null);
            bse.DefaultLicenseValidator = DefaultLicenseValidator;
            ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, null, null, "p", null, null, null, DefaultLicenseValidator);
            pse.DefaultLicenseValidator = DefaultLicenseValidator;
            TargetStartedEventArgs trse = new TargetStartedEventArgs(null, null, "t", null, null);
            trse.DefaultLicenseValidator = DefaultLicenseValidator;
            TaskStartedEventArgs tase = new TaskStartedEventArgs(null, null, null, null, "task");
            tase.DefaultLicenseValidator = DefaultLicenseValidator;
            BuildMessageEventArgs bmea = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
            bmea.DefaultLicenseValidator = DefaultLicenseValidator;
            BuildWarningEventArgs bwea = new BuildWarningEventArgs(null, null, null, 0, 0, 0, 0, null, null, null);
            bwea.DefaultLicenseValidator = DefaultLicenseValidator;
            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter(null, null, null, 0, 0, 0, 0, null, null, null);
            beea.DefaultLicenseValidator = DefaultLicenseValidator;
            TaskFinishedEventArgs trfea = new TaskFinishedEventArgs(null, null, null, null, "task", true);
            trfea.DefaultLicenseValidator = DefaultLicenseValidator;
            TargetFinishedEventArgs tafea = new TargetFinishedEventArgs(null, null, "t", null, null, true);
            tafea.DefaultLicenseValidator = DefaultLicenseValidator;
            ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs(null, null, "p", true);
            pfea.DefaultLicenseValidator = DefaultLicenseValidator;
            BuildFinishedEventArgs bfea = new BuildFinishedEventArgs(null, null, true);
            bfea.DefaultLicenseValidator = DefaultLicenseValidator;
            MyCustomCalcArrayWrappingScalar2 mcea = new MyCustomCalcArrayWrappingScalar2();
            mcea.DefaultLicenseValidator = DefaultLicenseValidator;


            // Not all parameters are null here, but that's fine, we assume the engine will never
            // fire a ProjectStarted without a project name, etc.
            es.Consume(bse);
            es.Consume(pse);
            es.Consume(trse);
            es.Consume(tase);
            es.Consume(bmea);
            es.Consume(bwea);
            es.Consume(beea);
            es.Consume(trfea);
            es.Consume(tafea);
            es.Consume(pfea);
            es.Consume(bfea);
            es.Consume(bfea);
            es.Consume(bfea);
            es.Consume(mcea);
            // No exception raised
        }

        [TestMethod]
        public void TestVerbosityLessThan()
        {
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new SerialConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(true,
                (new SerialConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Quiet)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Minimal)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Normal)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(false,
                (new ParallelConsoleLogger(LoggerVerbosity.Detailed)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));

            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Quiet));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Minimal));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Normal));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Detailed));
            Assert.AreEqual(true,
                (new ParallelConsoleLogger(LoggerVerbosity.Diagnostic)).IsVerbosityAtLeast(LoggerVerbosity.Diagnostic));
        }

        /// <summary>
        /// Test of single message printing
        /// </summary>
        [TestMethod]
        public void SingleMessageTests_quiet_low()
        {
            SingleMessageTest(LoggerVerbosity.Quiet,
                             MessageImportance.Low, false);
        }

        [TestMethod]
        public void SingleMessageTests_quiet_medium()
        {
            SingleMessageTest(LoggerVerbosity.Quiet,
                             MessageImportance.Normal, false);
        }

        [TestMethod]
        public void SingleMessageTests_quiet_high()
        {
            SingleMessageTest(LoggerVerbosity.Quiet,
                             MessageImportance.High, false);
        }

        [TestMethod]
        public void SingleMessageTests_medium_low()
        {
            SingleMessageTest(LoggerVerbosity.Minimal,
                             MessageImportance.Low, false);
        }

        [TestMethod]
        public void SingleMessageTests_medium_medium()
        {
            SingleMessageTest(LoggerVerbosity.Minimal,
                             MessageImportance.Normal, false);
        }

        [TestMethod]
        public void SingleMessageTests_medium_high()
        {
            SingleMessageTest(LoggerVerbosity.Minimal,
                             MessageImportance.High, true);
        }

        [TestMethod]
        public void SingleMessageTests_normal_low()
        {
            SingleMessageTest(LoggerVerbosity.Normal,
                             MessageImportance.Low, false);
        }

        [TestMethod]
        public void SingleMessageTests_normal_medium()
        {
            SingleMessageTest(LoggerVerbosity.Normal,
                             MessageImportance.Normal, true);
        }

        [TestMethod]
        public void SingleMessageTests_normal_high()
        {
            SingleMessageTest(LoggerVerbosity.Normal,
                             MessageImportance.High, true);
        }

        [TestMethod]
        public void SingleMessageTests_detailed_low()
        {
            SingleMessageTest(LoggerVerbosity.Detailed,
                             MessageImportance.Low, true);
        }

        [TestMethod]
        public void SingleMessageTests_detailed_medium()
        {
            SingleMessageTest(LoggerVerbosity.Detailed,
                             MessageImportance.Normal, true);
        }

        [TestMethod]
        public void SingleMessageTests_detailed_high()
        {
            SingleMessageTest(LoggerVerbosity.Detailed,
                             MessageImportance.High, true);
        }

        [TestMethod]
        public void SingleMessageTests_diagnostic_low()
        {
            SingleMessageTest(LoggerVerbosity.Diagnostic,
                             MessageImportance.Low, true);
        }

        [TestMethod]
        public void SingleMessageTests_diagnostic_medium()
        {
            SingleMessageTest(LoggerVerbosity.Diagnostic,
                             MessageImportance.Normal, true);
        }

        [TestMethod]
        public void SingleMessageTests_diagnostic_high()
        {
            SingleMessageTest(LoggerVerbosity.Diagnostic,
                             MessageImportance.High, true);
        }

        [TestMethod]
        public void ErrorColorTest()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();

            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es);

            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC", "31415", "file.vb", 42, 0, 0, 0, "Some long message", "help", "sender");
            es.Consume(beea);
            Assert.AreEqual("<red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine + "<reset color>", sc.ToString());
        }

        [TestMethod]
        public void ErrorColorTestParallel()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();

            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es, 4);

            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            beea.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

            es.Consume(beea);

            Assert.AreEqual(
               "<red>file.vb(42): VBC error 31415: Some long message" +
               Environment.NewLine + "<reset color>",
               sc.ToString());
        }

        [TestMethod]
        public void WarningColorTest()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es);

            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(bwea);

            Assert.AreEqual(
               "<yellow>file.vb(42): VBC warning 31415: Some long message" +
               Environment.NewLine + "<reset color>",
               sc.ToString());
        }

        [TestMethod]
        public void WarningColorTestParallel()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es, 2);

            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            bwea.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
            es.Consume(bwea);

            Assert.AreEqual(
               "<yellow>file.vb(42): VBC warning 31415: Some long message" +
               Environment.NewLine + "<reset color>",
               sc.ToString());
        }

        [TestMethod]
        public void LowMessageColorTest()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, sc.SetColor,
                                                sc.ResetColor);
            L.Initialize(es);

            BuildMessageEventArgs msg =
                new BuildMessageEventArgs("text", "help", "sender",
                                          MessageImportance.Low);

            es.Consume(msg);

            Assert.AreEqual(
               "<darkgray>text" +
               Environment.NewLine + "<reset color>",
               sc.ToString());
        }

        [TestMethod]
        public void TestQuietWithHighMessage()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor,
                                                    sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 1, 1, 1));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                BuildMessageEventArgs bmea = new BuildMessageEventArgs("foo!", null, "sender", MessageImportance.High);
                bmea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bmea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Assert.AreEqual(String.Empty, sc.ToString());
            }
        }

        [TestMethod]
        public void TestQuietWithError()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 2, 3, 4));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");

                beea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                if (i == 1)
                {
                    Assert.AreEqual(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>",
                            sc.ToString());
                }
                else
                {
                    Assert.AreEqual(
                            "<red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine + "<reset color>",
                            sc.ToString());
                }
            }
        }

        /// <summary>
        /// Quiet build with a warning; project finished should appear
        /// but not target finished
        /// </summary>
        [TestMethod]
        public void TestQuietWithWarning()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 2, 3, 4));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                if (i == 1)
                {
                    Assert.AreEqual(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>",
                            sc.ToString());
                }
                else
                {
                    Assert.AreEqual(
                            "<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>",
                            sc.ToString());
                }
            }
        }

        /// <summary>
        /// Minimal with no errors or warnings should emit nothing.
        /// </summary>
        [TestMethod]
        public void TestMinimalWithNormalMessage()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                    sc.Write, sc.SetColor,
                                                    sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 1, 1, 1));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                BuildMessageEventArgs bmea = new BuildMessageEventArgs("foo!", null, "sender", MessageImportance.Normal);
                bmea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bmea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Assert.AreEqual(String.Empty, sc.ToString());
            }
        }

        /// <summary>
        /// Minimal with error should emit project started, the error, and project finished
        /// </summary>
        [TestMethod]
        public void TestMinimalWithError()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 2, 3, 4));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");

                beea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                if (i == 1)
                {
                    Assert.AreEqual(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>",
                            sc.ToString());
                }
                else
                {
                    Assert.AreEqual(
                            "<red>file.vb(42): VBC error 31415: Some long message" + Environment.NewLine + "<reset color>",
                            sc.ToString());
                }
            }
        }

        /// <summary>
        /// Minimal with warning should emit project started, the warning, and project finished
        /// </summary>
        [TestMethod]
        public void TestMinimalWithWarning()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 2, 3, 4));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                es.Consume(bfea);

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                if (i == 1)
                {
                    Assert.AreEqual(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>",
                            sc.ToString());
                }
                else
                {
                    Assert.AreEqual(
                            "<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>",
                            sc.ToString());
                }
            }
        }

        /// <summary>
        /// Minimal with warning should emit project started, the warning, and project finished
        /// </summary>
        [TestMethod]
        public void TestDirectEventHandlers()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Quiet,
                                                    sc.Write, sc.SetColor, sc.ResetColor);
                L.Initialize(es, i);

                DefaultLicenseValidator DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);

                BuildStartedEventArgs bse = new BuildStartedEventArgs("bs", null);
                bse.DefaultLicenseValidator = DefaultLicenseValidator;
                L.BuildStartedHandler(null, bse);

                ProjectStartedEventArgs pse = new ProjectStartedEventArgs(-1, "ps", null, "fname", "", null, null, new DefaultLicenseValidator(1, 2, 3, 4));
                pse.DefaultLicenseValidator = DefaultLicenseValidator;
                L.ProjectStartedHandler(null, pse);

                TargetStartedEventArgs trse = new TargetStartedEventArgs("ts", null, "trname", "pfile", "tfile");
                trse.DefaultLicenseValidator = DefaultLicenseValidator;
                L.TargetStartedHandler(null, trse);

                TaskStartedEventArgs tase = new TaskStartedEventArgs("tks", null, "tname", "tfname", "tsname");
                tase.DefaultLicenseValidator = DefaultLicenseValidator;
                L.TaskStartedHandler(null, tase);

                BuildWarningEventArgs beea = new BuildWarningEventArgs("VBC",
                                "31415", "file.vb", 42, 0, 0, 0,
                                "Some long message", "help", "sender");


                beea.DefaultLicenseValidator = DefaultLicenseValidator;
                L.WarningHandler(null, beea);

                TaskFinishedEventArgs tafea = new TaskFinishedEventArgs("tkf", null, "fname", "tsname", "tfname", true);
                tafea.DefaultLicenseValidator = DefaultLicenseValidator;
                L.TaskFinishedHandler(null, tafea);

                TargetFinishedEventArgs trfea = new TargetFinishedEventArgs("tf", null, "trname", "fname", "tfile", true);
                trfea.DefaultLicenseValidator = DefaultLicenseValidator;
                L.TargetFinishedHandler(null, trfea);

                ProjectFinishedEventArgs pfea = new ProjectFinishedEventArgs("pf", null, "fname", true);
                pfea.DefaultLicenseValidator = DefaultLicenseValidator;
                L.ProjectFinishedHandler(null, pfea);

                BuildFinishedEventArgs bfea = new BuildFinishedEventArgs("bf", null, true);
                bfea.DefaultLicenseValidator = DefaultLicenseValidator;
                L.BuildFinishedHandler(null, bfea);

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                if (i == 1)
                {
                    Assert.AreEqual(
                            "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                            ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname") + Environment.NewLine + Environment.NewLine +
                            "<reset color><yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine +
                            "<reset color><cyan>pf" + Environment.NewLine +
                            "<reset color>",
                            sc.ToString());
                }
                else
                {
                    Assert.AreEqual(
                            "<yellow>file.vb(42): VBC warning 31415: Some long message" + Environment.NewLine + "<reset color>",
                            sc.ToString());
                }
            }
        }

        [TestMethod]
        public void SingleLineFormatNoop()
        {
            string s = "foo";
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should be a no-op
            Assert.AreEqual("foo" + Environment.NewLine, ss);
        }

        [TestMethod]
        public void MultilineFormatWindowsLineEndings()
        {
            string newline = "\r\n";
            string s = "foo" + newline + "bar" +
                       newline + "baz" + newline;
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 4);

            //should convert lines to system format
            Assert.AreEqual("    foo" + Environment.NewLine +
                                   "    bar" + Environment.NewLine +
                                   "    baz" + Environment.NewLine +
                                   "    " + Environment.NewLine, ss);
        }

        [TestMethod]
        public void MultilineFormatUnixLineEndings()
        {
            string s = "foo\nbar\nbaz\n";
            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should convert lines to system format
            Assert.AreEqual("foo" + Environment.NewLine +
                                   "bar" + Environment.NewLine +
                                   "baz" + Environment.NewLine + Environment.NewLine, ss);
        }

        [TestMethod]
        public void MultilineFormatMixedLineEndings()
        {
            string s = "foo" + "\r\n\r\n" + "bar" + "\n" + "baz" + "\n\r\n\n" +
                "jazz" + "\r\n" + "razz" + "\n\n" + "matazz" + "\n" + "end";

            SerialConsoleLogger cl = new SerialConsoleLogger();

            string ss = cl.IndentString(s, 0);

            //should convert lines to system format
            Assert.AreEqual("foo" + Environment.NewLine + Environment.NewLine +
                                   "bar" + Environment.NewLine +
                                   "baz" + Environment.NewLine + Environment.NewLine + Environment.NewLine +
                                   "jazz" + Environment.NewLine +
                                   "razz" + Environment.NewLine + Environment.NewLine +
                                   "matazz" + Environment.NewLine +
                                   "end" + Environment.NewLine, ss);
        }

        [TestMethod]
        public void NestedProjectMinimal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Minimal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 1);

            es.Consume(new BuildStartedEventArgs("bs", null));

            //Clear time dependant build started message
            sc.Clear();

            es.Consume(new ProjectStartedEventArgs("ps1", null, "fname1", "", null, null));

            es.Consume(new TargetStartedEventArgs("ts", null,
                                                     "trname", "fname", "tfile"));

            es.Consume(new ProjectStartedEventArgs("ps2", null, "fname2", "", null, null));

            Assert.AreEqual(string.Empty, sc.ToString());

            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            Assert.AreEqual(
                "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname1") + Environment.NewLine +
                                        Environment.NewLine + "<reset color>" +
                "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                ResourceUtilities.FormatResourceString("ProjectStartedPrefixForNestedProjectWithDefaultTargets", "fname1", "fname2") + Environment.NewLine +
                                                      Environment.NewLine + "<reset color>" +
                "<red>" + "file.vb(42): VBC error 31415: Some long message" +
                                                      Environment.NewLine + "<reset color>",
                sc.ToString());
        }

        [TestMethod]
        public void NestedProjectNormal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es);

            es.Consume(new BuildStartedEventArgs("bs", null));


            //Clear time dependant build started message
            string expectedOutput = null;
            string actualOutput = null;
            sc.Clear();

            es.Consume(new ProjectStartedEventArgs("ps1", null, "fname1", "", null, null));

            #region Check
            expectedOutput =
                        "<cyan>" + BaseConsoleLogger.projectSeparatorLine + Environment.NewLine +
                        ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", "fname1") + Environment.NewLine +
                        Environment.NewLine + "<reset color>";
            actualOutput = sc.ToString();

            Assert.AreEqual(expectedOutput, actualOutput);
            Console.WriteLine("1 [" + expectedOutput + "] [" + actualOutput + "]");
            sc.Clear();
            #endregion

            es.Consume(new TargetStartedEventArgs("ts", null,
                                                     "tarname", "fname", "tfile"));
            #region Check
            expectedOutput = String.Empty;
            actualOutput = sc.ToString();

            Console.WriteLine("2 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

            es.Consume(new TaskStartedEventArgs("", "", "", "", "Exec"));
            es.Consume(new ProjectStartedEventArgs("ps2", null, "fname2", "", null, null));

            #region Check
            expectedOutput =
                "<cyan>" + ResourceUtilities.FormatResourceString("TargetStartedPrefix", "tarname") + Environment.NewLine + "<reset color>"
                + "<cyan>" + "    " + BaseConsoleLogger.projectSeparatorLine
                                          + Environment.NewLine +
                "    " + ResourceUtilities.FormatResourceString("ProjectStartedPrefixForNestedProjectWithDefaultTargets", "fname1", "fname2") + Environment.NewLine +
                Environment.NewLine + "<reset color>";
            actualOutput = sc.ToString();

            Console.WriteLine("3 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

            es.Consume(new ProjectFinishedEventArgs("pf2", null, "fname2", true));
            es.Consume(new TaskFinishedEventArgs("", "", "", "", "Exec", true));

            #region Check
            expectedOutput = String.Empty;
            actualOutput = sc.ToString();

            Console.WriteLine("4 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

            es.Consume(new TargetFinishedEventArgs("tf", null, "tarname", "fname", "tfile", true));

            #region Check
            expectedOutput = String.Empty;
            actualOutput = sc.ToString();

            Console.WriteLine("5 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

            es.Consume(new ProjectFinishedEventArgs("pf1", null, "fname1", true));

            #region Check
            expectedOutput = String.Empty;
            actualOutput = sc.ToString();

            Console.WriteLine("6 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

            es.Consume(new BuildFinishedEventArgs("bf", null, true));

            #region Check
            expectedOutput = "<green>" + Environment.NewLine + "bf" +
                        Environment.NewLine + "<reset color>" +
                "    " + ResourceUtilities.FormatResourceString("WarningCount", 0) +
                        Environment.NewLine + "<reset color>" +
                "    " + ResourceUtilities.FormatResourceString("ErrorCount", 0) +
                        Environment.NewLine + "<reset color>" +
                        Environment.NewLine;

            // Would like to add...
            //    + ResourceUtilities.FormatResourceString("TimeElapsed", String.Empty);
            // ...but this assumes that the time goes on the far right in every locale.

            actualOutput = sc.ToString().Substring(0, expectedOutput.Length);

            Console.WriteLine("7 [" + expectedOutput + "] [" + actualOutput + "]");
            Assert.AreEqual(expectedOutput, actualOutput);
            sc.Clear();
            #endregion

        }

        [TestMethod]
        public void CustomDisplayedAtDetailed()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Detailed,
                                                sc.Write, null, null);
            L.Initialize(es);

            MyCustomCalcArrayWrappingScalar c =
                    new MyCustomCalcArrayWrappingScalar("msg");

            es.Consume(c);

            Assert.AreEqual("msg" + Environment.NewLine,
                                   sc.ToString());
        }

        [TestMethod]
        public void CustomDisplayedAtDiagnosticMP()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Diagnostic,
                                                sc.Write, null, null);
            L.Initialize(es, 2);

            MyCustomCalcArrayWrappingScalar c =
                    new MyCustomCalcArrayWrappingScalar("msg");
            c.DefaultLicenseValidator = new DefaultLicenseValidator(1, 1, 1, 1);
            es.Consume(c);

            Assert.IsTrue(sc.ToString().Contains("msg"));
        }

        [TestMethod]
        public void CustomNotDisplayedAtNormal()
        {
            EventSourceSink es = new EventSourceSink();
            SimulatedConsole sc = new SimulatedConsole();
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, null, null);
            L.Initialize(es);

            MyCustomCalcArrayWrappingScalar c =
                    new MyCustomCalcArrayWrappingScalar("msg");

            es.Consume(c);

            Assert.AreEqual(String.Empty, sc.ToString());
        }

        /// <summary>
        /// Create some properties and log them
        /// </summary>
        /// <param name="cl"></param>
        /// <returns></returns>
        private void WriteAndValidateProperties(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            Hashtable properties = new Hashtable();
            properties.Add("prop1", "val1");
            properties.Add("prop2", "val2");
            properties.Add("pro(p3)", "va%3b%253b%3bl3");
            string prop1 = string.Empty;
            string prop2 = string.Empty;
            string prop3 = string.Empty;

            if (cl is SerialConsoleLogger)
            {
                ArrayList propertyList = ((SerialConsoleLogger)cl).ExtractPropertyList(properties);
                ((SerialConsoleLogger)cl).WriteProperties(propertyList);
                prop1 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "prop1", "val1");
                prop2 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "prop2", "val2");
                prop3 = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", "pro(p3)", "va;%3b;l3");
            }
            else
            {
                CalcArrayWrappingScalar buildEvent = new DialogWindowEditorToStringValueConverter("", "", "", 0, 0, 0, 0, "", "", "");
                buildEvent.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
                ((ParallelConsoleLogger)cl).WriteProperties(buildEvent, properties);
                prop1 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "prop1", "val1");
                prop2 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "prop2", "val2");
                prop3 = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", "pro(p3)", "va;%3b;l3");
            }
            string log = sc.ToString();

            Console.WriteLine("[" + log + "]");


            // Being careful not to make locale assumptions here, eg about sorting
            if (expectToSeeLogging)
            {
                Assert.IsTrue(log.Contains(prop1));
                Assert.IsTrue(log.Contains(prop2));
                Assert.IsTrue(log.Contains(prop3));
            }
            else
            {
                Assert.IsFalse(log.Contains(prop1));
                Assert.IsFalse(log.Contains(prop2));
                Assert.IsFalse(log.Contains(prop3));
            }
        }

        /// <summary>
        /// Basic test of properties list display
        /// </summary>
        [TestMethod]
        public void DisplayPropertiesList()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateProperties(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateProperties(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of properties list not being displayed except in Diagnostic
        /// </summary>
        [TestMethod]
        public void DoNotDisplayPropertiesListInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateProperties(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateProperties(cl2, sc, false);
        }


        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [TestMethod]
        public void DoNotDisplayEnvironmentInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteEnvironment(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteEnvironment(cl2, sc, false);
        }



        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [TestMethod]
        public void DisplayEnvironmentInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            cl.Parameters = "ShowEnvironment";
            cl.ParseParameters();
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);
            cl2.Parameters = "ShowEnvironment";
            cl2.ParseParameters();

            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [TestMethod]
        public void DisplayEnvironmentInDiagnostic()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [TestMethod]
        public void DoNotDisplayEnvironmentInMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);

            WriteEnvironment(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);

            WriteEnvironment(cl2, sc, false);
        }



        /// <summary>
        /// Basic test of environment list not being displayed except in Diagnostic or if the showenvironment flag is set
        /// </summary>
        [TestMethod]
        public void DisplayEnvironmentInMinimal()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            cl.Parameters = "ShowEnvironment";
            cl.ParseParameters();
            WriteEnvironment(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Minimal, sc.Write, null, null);
            cl2.Parameters = "ShowEnvironment";
            cl2.ParseParameters();

            WriteEnvironment(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of properties list not being displayed when disabled
        /// </summary>
        [TestMethod]
        public void DoNotDisplayPropertiesListIfDisabled()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl.Parameters = "noitemandpropertylist";
            cl.ParseParameters();

            WriteAndValidateProperties(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateProperties(cl, sc, false);
        }


        /// <summary>
        /// Create some items and log them
        /// </summary>
        private void WriteEnvironment(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            cl.WriteEnvironment(_environment);
            string log = sc.ToString();
            Console.WriteLine("[" + log + "]");

            // Being careful not to make locale assumptions here, eg about sorting
            foreach (KeyValuePair<string, string> kvp in _environment)
            {
                string message = String.Empty;
                if (cl is ParallelConsoleLogger)
                {
                    message = String.Format(CultureInfo.CurrentCulture, "{0} = {1}", kvp.Key, kvp.Value);
                }
                else
                {
                    message = String.Format(CultureInfo.CurrentCulture, "{0,-30} = {1}", kvp.Key, kvp.Value);
                }

                if (expectToSeeLogging)
                {
                    Assert.IsTrue(log.Contains(message));
                }
                else
                {
                    Assert.IsFalse(log.Contains(message));
                }
            }
        }

        /// <summary>
        /// Create some items and log them
        /// </summary>
        /// <returns></returns>
        private void WriteAndValidateItems(BaseConsoleLogger cl, SimulatedConsole sc, bool expectToSeeLogging)
        {
            Hashtable items = new Hashtable();
            items.Add("type", (ITaskItem2)new TaskItem("spec", String.Empty));
            items.Add("type2", (ITaskItem2)new TaskItem("spec2", String.Empty));

            // ItemSpecs are expected to be escaped coming in
            ITaskItem2 taskItem3 = new TaskItem("%28spec%3b3", String.Empty);

            // As are metadata, when set with "SetMetadata"
            taskItem3.SetMetadata("f)oo", "%21%40%23");

            items.Add("type(3)", taskItem3);

            string item1type = string.Empty;
            string item2type = string.Empty;
            string item3type = string.Empty;
            string item1spec = string.Empty;
            string item2spec = string.Empty;
            string item3spec = string.Empty;
            string item3metadatum = string.Empty;

            if (cl is SerialConsoleLogger)
            {
                SortedList itemList = ((SerialConsoleLogger)cl).ExtractItemList(items);
                ((SerialConsoleLogger)cl).WriteItems(itemList);
                item1spec = "spec" + Environment.NewLine;
                item2spec = "spec2" + Environment.NewLine;
                item3spec = "(spec;3" + Environment.NewLine;
                item3metadatum = "f)oo = !@#" + Environment.NewLine;
            }
            else
            {
                CalcArrayWrappingScalar buildEvent = new DialogWindowEditorToStringValueConverter("", "", "", 0, 0, 0, 0, "", "", "");
                buildEvent.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
                ((ParallelConsoleLogger)cl).WriteItems(buildEvent, items);
                item1spec = Environment.NewLine + "    spec" + Environment.NewLine;
                item2spec = Environment.NewLine + "    spec2" + Environment.NewLine;
                item3spec = Environment.NewLine + "    (spec;3" + Environment.NewLine;
            }

            item1type = "type" + Environment.NewLine;
            item2type = "type2" + Environment.NewLine;
            item3type = "type(3)" + Environment.NewLine;

            string log = sc.ToString();

            Console.WriteLine("[" + log + "]");



            // Being careful not to make locale assumptions here, eg about sorting
            if (expectToSeeLogging)
            {
                Assert.IsTrue(log.Contains(item1type));
                Assert.IsTrue(log.Contains(item2type));
                Assert.IsTrue(log.Contains(item3type));
                Assert.IsTrue(log.Contains(item1spec));
                Assert.IsTrue(log.Contains(item2spec));
                Assert.IsTrue(log.Contains(item3spec));

                if (!String.Equals(item3metadatum, String.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsTrue(log.Contains(item3metadatum));
                }
            }
            else
            {
                Assert.IsFalse(log.Contains(item1type));
                Assert.IsFalse(log.Contains(item2type));
                Assert.IsFalse(log.Contains(item3type));
                Assert.IsFalse(log.Contains(item1spec));
                Assert.IsFalse(log.Contains(item2spec));
                Assert.IsFalse(log.Contains(item3type));

                if (!String.Equals(item3metadatum, String.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    Assert.IsFalse(log.Contains(item3metadatum));
                }
            }
        }

        /// <summary>
        /// Verify passing in an empty item list does not print anything out
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public void WriteItemsEmptyList()
        {
            Hashtable items = new Hashtable();

            for (int i = 0; i < 2; i++)
            {
                BaseConsoleLogger cl = null;
                SimulatedConsole sc = new SimulatedConsole();
                if (i == 0)
                {
                    cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }
                else
                {
                    cl = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }

                if (cl is SerialConsoleLogger)
                {
                    SortedList itemList = ((SerialConsoleLogger)cl).ExtractItemList(items);
                    ((SerialConsoleLogger)cl).WriteItems(itemList);
                }
                else
                {
                    CalcArrayWrappingScalar buildEvent = new DialogWindowEditorToStringValueConverter("", "", "", 0, 0, 0, 0, "", "", "");
                    buildEvent.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
                    ((ParallelConsoleLogger)cl).WriteItems(buildEvent, items);
                }

                string log = sc.ToString();

                // There should be nothing in the log
                Assert.IsTrue(log.Length == 0, "Iteration of I: " + i);
                Console.WriteLine("Iteration of i: " + i + "[" + log + "]");
            }
        }

        /// <summary>
        /// Verify passing in an empty item list does not print anything out
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public void WritePropertiesEmptyList()
        {
            Hashtable properties = new Hashtable();


            for (int i = 0; i < 2; i++)
            {
                BaseConsoleLogger cl = null;
                SimulatedConsole sc = new SimulatedConsole();
                if (i == 0)
                {
                    cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }
                else
                {
                    cl = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
                }

                if (cl is SerialConsoleLogger)
                {
                    ArrayList propertyList = ((SerialConsoleLogger)cl).ExtractPropertyList(properties);
                    ((SerialConsoleLogger)cl).WriteProperties(propertyList);
                }
                else
                {
                    CalcArrayWrappingScalar buildEvent = new DialogWindowEditorToStringValueConverter("", "", "", 0, 0, 0, 0, "", "", "");
                    buildEvent.DefaultLicenseValidator = new DefaultLicenseValidator(1, 2, 3, 4);
                    ((ParallelConsoleLogger)cl).WriteProperties(buildEvent, properties);
                }

                string log = sc.ToString();

                // There should be nothing in the log
                Assert.IsTrue(log.Length == 0, "Iteration of I: " + i);
                Console.WriteLine("Iteration of i: " + i + "[" + log + "]");
            }
        }

        /// <summary>
        /// Basic test of item list display
        /// </summary>
        [TestMethod]
        public void DisplayItemsList()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateItems(cl, sc, true);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);

            WriteAndValidateItems(cl2, sc, true);
        }

        /// <summary>
        /// Basic test of item list not being displayed except in Diagnostic
        /// </summary>
        [TestMethod]
        public void DoNotDisplayItemListInDetailed()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateItems(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Detailed, sc.Write, null, null);

            WriteAndValidateItems(cl2, sc, false);
        }

        /// <summary>
        /// Basic test of item list not being displayed when disabled
        /// </summary>
        [TestMethod]
        public void DoNotDisplayItemListIfDisabled()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger cl = new SerialConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl.Parameters = "noitemandpropertylist";
            cl.ParseParameters();

            WriteAndValidateItems(cl, sc, false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateItems(cl2, sc, false);
        }

        [TestMethod]
        public void ParametersEmptyTests()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger L = new SerialConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L.Parameters = "";
            L.ParseParameters();
            Assert.IsTrue(L.ShowSummary == false);

            L.Parameters = null;
            L.ParseParameters();
            Assert.IsTrue(L.ShowSummary == false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger cl2 = new ParallelConsoleLogger(LoggerVerbosity.Diagnostic, sc.Write, null, null);
            cl2.Parameters = "noitemandpropertylist";
            cl2.ParseParameters();

            WriteAndValidateItems(cl2, sc, false);
        }

        [TestMethod]
        public void ParametersParsingTests()
        {
            SimulatedConsole sc = new SimulatedConsole();
            SerialConsoleLogger L = new SerialConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L.Parameters = "NoSuMmaRy";
            L.ParseParameters();
            Assert.IsTrue(L.ShowSummary == false);

            L.Parameters = ";;NoSuMmaRy;";
            L.ParseParameters();
            Assert.IsTrue(L.ShowSummary == false);

            sc = new SimulatedConsole();
            ParallelConsoleLogger L2 = new ParallelConsoleLogger(LoggerVerbosity.Normal, sc.Write, null, null);

            L2.Parameters = "NoSuMmaRy";
            L2.ParseParameters();
            Assert.IsTrue(L2.ShowSummary == false);

            L2.Parameters = ";;NoSuMmaRy;";
            L2.ParseParameters();
            Assert.IsTrue(L2.ShowSummary == false);
        }

        /// <summary>
        /// ResetConsoleLoggerState should reset the state of the console logger
        /// </summary>
        [TestMethod]
        public void ResetConsoleLoggerStateTestBasic()
        {
            // Create an event source
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();

            // error and warning string for 1 error and 1 warning
            // errorString = 1 Error(s)
            // warningString = 1 Warning(s)
            string errorString = ResourceUtilities.FormatResourceString("ErrorCount", 1);
            string warningString = ResourceUtilities.FormatResourceString("WarningCount", 1);

            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            // Initialize ConsoleLogger
            L.Initialize(es);

            // BuildStarted Event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // Introduce a warning
            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                            "31415", "file.vb", 42, 0, 0, 0,
                            "Some long message", "help", "sender");

            es.Consume(bwea);

            // Introduce an error
            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            // BuildFinished Event
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));

            // Log so far
            string actualLog = sc.ToString();

            Console.WriteLine("==");
            Console.WriteLine(sc.ToString());
            Console.WriteLine("==");

            // Verify that the log has correct error and warning string
            Assert.IsTrue(actualLog.Contains(errorString));
            Assert.IsTrue(actualLog.Contains(warningString));
            Assert.IsTrue(actualLog.Contains("<red>"));
            Assert.IsTrue(actualLog.Contains("<yellow>"));

            // Clear the log obtained so far
            sc.Clear();

            // BuildStarted event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // BuildFinished 
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));
            // Log so far
            actualLog = sc.ToString();

            Console.WriteLine("==");
            Console.WriteLine(sc.ToString());
            Console.WriteLine("==");

            // Verify that the error and warning from the previous build is not
            // reported in the subsequent build
            Assert.IsFalse(actualLog.Contains(errorString));
            Assert.IsFalse(actualLog.Contains(warningString));
            Assert.IsFalse(actualLog.Contains("<red>"));
            Assert.IsFalse(actualLog.Contains("<yellow>"));

            // errorString = 0 Error(s)
            // warningString = 0 Warning(s)
            errorString = ResourceUtilities.FormatResourceString("ErrorCount", 0);
            warningString = ResourceUtilities.FormatResourceString("WarningCount", 0);

            // Verify that the log has correct error and warning string
            Assert.IsTrue(actualLog.Contains(errorString));
            Assert.IsTrue(actualLog.Contains(warningString));
        }

        /// <summary>
        /// ConsoleLogger::Initialize() should reset the state of the console logger
        /// </summary>
        [TestMethod]
        public void ResetConsoleLoggerState_Initialize()
        {
            // Create an event source
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();

            // error and warning string for 1 error and 1 warning
            // errorString = 1 Error(s)
            // warningString = 1 Warning(s)
            string errorString = ResourceUtilities.FormatResourceString("ErrorCount", 1);
            string warningString = ResourceUtilities.FormatResourceString("WarningCount", 1);

            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal,
                                                sc.Write, sc.SetColor, sc.ResetColor);
            // Initialize ConsoleLogger
            L.Initialize(es);

            // BuildStarted Event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // Introduce a warning
            BuildWarningEventArgs bwea = new BuildWarningEventArgs("VBC",
                            "31415", "file.vb", 42, 0, 0, 0,
                            "Some long message", "help", "sender");

            es.Consume(bwea);

            // Introduce an error
            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter("VBC",
                        "31415", "file.vb", 42, 0, 0, 0,
                        "Some long message", "help", "sender");

            es.Consume(beea);

            // NOTE: We don't call the es.RaiseBuildFinishedEvent(...) here as this 
            // would call ResetConsoleLoggerState and we will fail to detect if Initialize() 
            // is not calling it.

            // Log so far
            string actualLog = sc.ToString();

            Console.WriteLine("==");
            Console.WriteLine(sc.ToString());
            Console.WriteLine("==");

            // Verify that the log has correct error and warning string
            Assert.IsTrue(actualLog.Contains("<red>"));
            Assert.IsTrue(actualLog.Contains("<yellow>"));

            // Clear the log obtained so far
            sc.Clear();

            //Initilialize (This should call ResetConsoleLoggerState(...))
            L.Initialize(es);

            // BuildStarted event
            es.Consume(new BuildStartedEventArgs("bs", null));

            // BuildFinished 
            es.Consume(new BuildFinishedEventArgs("bf",
                                                     null, true));
            // Log so far
            actualLog = sc.ToString();

            Console.WriteLine("==");
            Console.WriteLine(sc.ToString());
            Console.WriteLine("==");

            // Verify that the error and warning from the previous build is not
            // reported in the subsequent build
            Assert.IsFalse(actualLog.Contains("<red>"));
            Assert.IsFalse(actualLog.Contains("<yellow>"));

            // errorString = 0 Error(s)
            errorString = ResourceUtilities.FormatResourceString("ErrorCount", 0);
            // warningString = 0 Warning(s)
            warningString = ResourceUtilities.FormatResourceString("WarningCount", 0);

            // Verify that the log has correct error and warning string
            Assert.IsTrue(actualLog.Contains(errorString));
            Assert.IsTrue(actualLog.Contains(warningString));
        }

        /// <summary>
        /// ResetConsoleLoggerState should reset PerformanceCounters
        /// </summary>
        [TestMethod]
        public void ResetConsoleLoggerState_PerformanceCounters()
        {
            for (int i = 1; i <= 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                // Create a ConsoleLogger with Normal verbosity
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
                // Initialize ConsoleLogger
                L.Parameters = "Performancesummary";
                L.Initialize(es, i);
                // prjPerfString = Project Performance Summary:
                string prjPerfString = ResourceUtilities.FormatResourceString("ProjectPerformanceSummary", null);
                // targetPerfString = Target Performance Summary:
                string targetPerfString = ResourceUtilities.FormatResourceString("TargetPerformanceSummary", null);
                // taskPerfString = Task Performance Summary:
                string taskPerfString = ResourceUtilities.FormatResourceString("TaskPerformanceSummary", null);

                // BuildStarted Event
                es.Consume(new BuildStartedEventArgs("bs", null));
                //Project Started Event
                ProjectStartedEventArgs project1Started = new ProjectStartedEventArgs(1, null, null, "p", "t", null, null, new DefaultLicenseValidator(DefaultLicenseValidator.InvalidNodeId, DefaultLicenseValidator.InvalidTargetId, DefaultLicenseValidator.InvalidProjectContextId, DefaultLicenseValidator.InvalidTaskId));
                project1Started.DefaultLicenseValidator = new DefaultLicenseValidator(1, 1, 1, 1);
                es.Consume(project1Started);
                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // TargetStarted Event
                es.Consume(targetStarted1);

                TaskStartedEventArgs taskStarted1 = new TaskStartedEventArgs(null, null, null, null, "task");
                taskStarted1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // TaskStarted Event 
                es.Consume(taskStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
                messsage1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // Message Event
                es.Consume(messsage1);
                TaskFinishedEventArgs taskFinished1 = new TaskFinishedEventArgs(null, null, null, null, "task", true);
                taskFinished1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // TaskFinished Event
                es.Consume(taskFinished1);

                TargetFinishedEventArgs targetFinished1 = new TargetFinishedEventArgs(null, null, "t", null, null, true);
                targetFinished1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // TargetFinished Event
                es.Consume(targetFinished1);

                ProjectStartedEventArgs project2Started = new ProjectStartedEventArgs(2, null, null, "p2", "t2", null, null, project1Started.DefaultLicenseValidator);
                //Project Started Event
                project2Started.DefaultLicenseValidator = new DefaultLicenseValidator(2, 2, 2, 2);
                es.Consume(project2Started);
                TargetStartedEventArgs targetStarted2 = new TargetStartedEventArgs(null, null, "t2", null, null);
                targetStarted2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // TargetStarted Event
                es.Consume(targetStarted2);

                TaskStartedEventArgs taskStarted2 = new TaskStartedEventArgs(null, null, null, null, "task2");
                taskStarted2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // TaskStarted Event 
                es.Consume(taskStarted2);

                BuildMessageEventArgs messsage2 = new BuildMessageEventArgs(null, null, null, MessageImportance.High);
                messsage2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // Message Event
                es.Consume(messsage2);
                TaskFinishedEventArgs taskFinished2 = new TaskFinishedEventArgs(null, null, null, null, "task2", true);
                taskFinished2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // TaskFinished Event
                es.Consume(taskFinished2);

                TargetFinishedEventArgs targetFinished2 = new TargetFinishedEventArgs(null, null, "t2", null, null, true);
                targetFinished2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // TargetFinished Event
                es.Consume(targetFinished2);

                ProjectFinishedEventArgs finished2 = new ProjectFinishedEventArgs(null, null, "p2", true);
                finished2.DefaultLicenseValidator = project2Started.DefaultLicenseValidator;
                // ProjectFinished Event
                es.Consume(finished2);            // BuildFinished Event

                ProjectFinishedEventArgs finished1 = new ProjectFinishedEventArgs(null, null, "p", true);
                finished1.DefaultLicenseValidator = project1Started.DefaultLicenseValidator;
                // ProjectFinished Event
                es.Consume(finished1);            // BuildFinished Event
                es.Consume(new BuildFinishedEventArgs("bf",
                                                         null, true));
                // Log so far
                string actualLog = sc.ToString();

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                // Verify that the log has perf summary
                // Project perf summary
                Assert.IsTrue(actualLog.Contains(prjPerfString));
                // Target perf summary
                Assert.IsTrue(actualLog.Contains(targetPerfString));
                // Task Perf summary
                Assert.IsTrue(actualLog.Contains(taskPerfString));

                // Clear the log obtained so far
                sc.Clear();

                // BuildStarted event
                es.Consume(new BuildStartedEventArgs("bs", null));
                // BuildFinished 
                es.Consume(new BuildFinishedEventArgs("bf",
                                                         null, true));
                // Log so far
                actualLog = sc.ToString();

                Console.WriteLine("==");
                Console.WriteLine(sc.ToString());
                Console.WriteLine("==");

                // Verify that the log doesn't have perf summary
                Assert.IsFalse(actualLog.Contains(prjPerfString));
                Assert.IsFalse(actualLog.Contains(targetPerfString));
                Assert.IsFalse(actualLog.Contains(taskPerfString));
            }
        }


        [TestMethod]
        public void DeferredMessages()
        {
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();
            // Create a ConsoleLogger with Detailed verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Detailed, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            TaskCommandLineEventArgs messsage1 = new TaskCommandLineEventArgs("Message", null, MessageImportance.High);
            messsage1.DefaultLicenseValidator = new DefaultLicenseValidator(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage1);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            string actualLog = sc.ToString();
            Assert.IsTrue(actualLog.Contains(ResourceUtilities.FormatResourceString("DeferredMessages")));

            es = new EventSourceSink();
            sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            BuildMessageEventArgs messsage2 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage2.DefaultLicenseValidator = new DefaultLicenseValidator(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage2);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            actualLog = sc.ToString();
            Assert.IsTrue(actualLog.Contains(ResourceUtilities.FormatResourceString("DeferredMessages")));

            es = new EventSourceSink();
            sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            messsage2 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage2.DefaultLicenseValidator = new DefaultLicenseValidator(1, 1, 1, 1);
            // Message Event
            es.Consume(messsage2);
            ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, messsage1.DefaultLicenseValidator);
            project.DefaultLicenseValidator = messsage1.DefaultLicenseValidator;
            es.Consume(project);
            es.Consume(new BuildFinishedEventArgs("bf", null, true));
            actualLog = sc.ToString();
            Assert.IsTrue(actualLog.Contains("Message"));
        }

        [TestMethod]
        public void VerifyMPLoggerSwitch()
        {
            for (int i = 0; i < 2; i++)
            {
                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                // Create a ConsoleLogger with Normal verbosity
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
                //Make sure the MPLogger switch will property work on both Initialize methods
                L.Parameters = "EnableMPLogging";
                if (i == 0)
                {
                    L.Initialize(es, 1);
                }
                else
                {
                    L.Initialize(es);
                }
                es.Consume(new BuildStartedEventArgs("bs", null));
                DefaultLicenseValidator context = new DefaultLicenseValidator(1, 1, 1, 1);
                DefaultLicenseValidator context2 = new DefaultLicenseValidator(2, 2, 2, 2);

                ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
                project.DefaultLicenseValidator = context;
                es.Consume(project);

                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.DefaultLicenseValidator = context;
                es.Consume(targetStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
                messsage1.DefaultLicenseValidator = context;
                es.Consume(messsage1);
                string actualLog = sc.ToString();
                string resourceString = ResourceUtilities.FormatResourceString("ProjectStartedTopLevelProjectWithTargetNames", "None", 1, "Build");
                Assert.IsTrue(actualLog.Contains(resourceString));
            }
        }

        [TestMethod]
        public void TestPrintTargetNamePerMessage()
        {
            EventSourceSink es = new EventSourceSink();
            //Create a simulated console
            SimulatedConsole sc = new SimulatedConsole();
            // Create a ConsoleLogger with Normal verbosity
            ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);
            L.Initialize(es, 2);
            es.Consume(new BuildStartedEventArgs("bs", null));
            DefaultLicenseValidator context = new DefaultLicenseValidator(1, 1, 1, 1);
            DefaultLicenseValidator context2 = new DefaultLicenseValidator(2, 2, 2, 2);

            ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
            project.DefaultLicenseValidator = context;
            es.Consume(project);

            ProjectStartedEventArgs project2 = new ProjectStartedEventArgs(2, "Hello,", "HI", "None", "Build", null, null, context2);
            project2.DefaultLicenseValidator = context2;
            es.Consume(project2);

            TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
            targetStarted1.DefaultLicenseValidator = context;
            es.Consume(targetStarted1);

            TargetStartedEventArgs targetStarted2 = new TargetStartedEventArgs(null, null, "t2", null, null);
            targetStarted2.DefaultLicenseValidator = context2;
            es.Consume(targetStarted2);

            BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
            messsage1.DefaultLicenseValidator = context;
            BuildMessageEventArgs messsage2 = new BuildMessageEventArgs("Message2", null, null, MessageImportance.High);
            messsage2.DefaultLicenseValidator = context2;
            BuildMessageEventArgs messsage3 = new BuildMessageEventArgs("Message3", null, null, MessageImportance.High);
            messsage3.DefaultLicenseValidator = context;
            es.Consume(messsage1);
            es.Consume(messsage2);
            es.Consume(messsage3);
            string actualLog = sc.ToString();
            Assert.IsTrue(actualLog.Contains("t:"));
        }

        /// <summary>
        /// Verify that in the MP case and the older serial logger that there is no extra newline after the project done event. 
        /// We cannot verify there is a newline after the project done event for the MP single proc log because
        /// nunit is showing up as an unknown output type, this causes us to not print the newline because we think it may be to a 
        /// text file.
        /// </summary>
        [TestMethod]
        public void TestNewLineAfterProjectFinished()
        {
            bool runningWithCharDevice = IsRunningWithCharacterFileType();
            for (int i = 0; i < 3; i++)
            {
                Console.Out.WriteLine("Iteration of I is {" + i + "}");


                EventSourceSink es = new EventSourceSink();
                //Create a simulated console
                SimulatedConsole sc = new SimulatedConsole();
                ConsoleLogger L = new ConsoleLogger(LoggerVerbosity.Normal, sc.Write, sc.SetColor, sc.ResetColor);

                if (i < 2)
                {
                    // On the second pass through use the MP single proc logger
                    if (i == 1)
                    {
                        L.Parameters = "EnableMPLogging";
                    }
                    // Use the old single proc logger
                    L.Initialize(es, 1);
                }
                else
                {
                    // Use the parallel logger
                    L.Initialize(es, 2);
                }

                es.Consume(new BuildStartedEventArgs("bs", null));
                DefaultLicenseValidator context = new DefaultLicenseValidator(1, 1, 1, 1);

                ProjectStartedEventArgs project = new ProjectStartedEventArgs(1, "Hello,", "HI", "None", "Build", null, null, context);
                project.DefaultLicenseValidator = context;
                es.Consume(project);

                TargetStartedEventArgs targetStarted1 = new TargetStartedEventArgs(null, null, "t", null, null);
                targetStarted1.DefaultLicenseValidator = context;
                es.Consume(targetStarted1);

                BuildMessageEventArgs messsage1 = new BuildMessageEventArgs("Message", null, null, MessageImportance.High);
                messsage1.DefaultLicenseValidator = context;
                es.Consume(messsage1);

                ProjectFinishedEventArgs projectFinished = new ProjectFinishedEventArgs("Finished,", "HI", "projectFile", true);
                projectFinished.DefaultLicenseValidator = context;
                es.Consume(projectFinished);

                string actualLog = sc.ToString();

                switch (i)
                {
                    case 0:
                        // There is no project finished event printed in normal verbosity
                        Assert.IsFalse(actualLog.Contains(projectFinished.Message));
                        break;
                    // We are in single proc but logging with multiproc logging add an extra new line to make the log more readable.
                    case 1:
                        Assert.IsTrue(actualLog.Contains(ResourceUtilities.FormatResourceString("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine));
                        if (runningWithCharDevice)
                        {
                            Assert.IsTrue(actualLog.Contains(ResourceUtilities.FormatResourceString("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine));
                        }
                        else
                        {
                            Assert.IsFalse(actualLog.Contains(ResourceUtilities.FormatResourceString("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine));
                        }
                        break;
                    case 2:
                        Assert.IsFalse(actualLog.Contains(ResourceUtilities.FormatResourceString("ProjectFinishedPrefixWithTargetNamesMultiProc", "None", "Build") + Environment.NewLine + Environment.NewLine));
                        break;
                }
            }
        }

        /// <summary>
        /// Check to see what kind of device we are outputting the log to, is it a character device, a file, or something else
        /// this can be used by loggers to modify their outputs based on the device they are writing to
        /// </summary>
        internal bool IsRunningWithCharacterFileType()
        {
            // Get the std out handle
            IntPtr stdHandle = NativeMethodsShared.GetStdHandle(NativeMethodsShared.STD_OUTPUT_HANDLE);

            if (stdHandle != Microsoft.Build.BackEnd.NativeMethods.InvalidHandle)
            {
                uint fileType = NativeMethodsShared.GetFileType(stdHandle);

                // The std out is a char type(LPT or Console)
                return fileType == NativeMethodsShared.FILE_TYPE_CHAR;
            }
            else
            {
                return false;
            }
        }
    }
}

