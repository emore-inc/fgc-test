﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Unit tests for the TargetEntry with a mock task builder.</summary>
//-----------------------------------------------------------------------

using System;
using System.Xml;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ElementLocation = Microsoft.Build.Construction.ElementLocation;
using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Shared;
using Microsoft.Build.Collections;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Execution;
using Microsoft.Build.BackEnd.Logging;
using System.Threading.Tasks;

namespace Microsoft.Build.UnitTests.BackEnd
{
    /// <summary>
    /// Test for the TargetEntry class used by the TargetBuilder.  This class does most of the
    /// actual work to build a target.
    /// </summary>
    [TestClass]
    public class TargetEntry_Tests : ITargetBuilderCallback
    {
        /// <summary>
        /// The component host.
        /// </summary>
        private MockHost _host;

        /// <summary>
        /// The node request id counter
        /// </summary>
        private int _nodeRequestId;

        /// <summary>
        /// Handles exceptions from the logging system.
        /// </summary>
        /// <param name="e">The exception</param>
        public void LoggingException(Exception e)
        {
        }

        /// <summary>
        /// Called prior to each test.
        /// </summary>
        [TestInitialize]
        public void SetUp()
        {
            _nodeRequestId = 1;
            _host = new MockHost();
            _host.OnLoggingThreadException += this.LoggingException;
        }

        /// <summary>
        /// Called after each test is run.
        /// </summary>
        [TestCleanup]
        public void TearDown()
        {
            File.Delete("testProject.proj");
            _host = null;
        }

        /// <summary>
        /// Tests a constructor with a null target.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestConstructorNullTarget()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            BuildRequestConfiguration config = new BuildRequestConfiguration(1, new BuildRequestData("foo", new Dictionary<string, string>(), "foo", new string[0], null), "2.0");
            BuildRequestEntry requestEntry = new BuildRequestEntry(CreateNewBuildRequest(1, new string[] { "foo" }), config);
            Lookup lookup = new Lookup(new ItemDictionary<ProjectItemInstance>(project.Items), new PropertyDictionary<ProjectPropertyInstance>(project.Properties), null);
            TargetEntry entry = new TargetEntry(requestEntry, this, null, lookup, null, _host, false);
        }

        /// <summary>
        /// Tests a constructor with a null lookup.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestConstructorNullLookup()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            BuildRequestConfiguration config = new BuildRequestConfiguration(1, new BuildRequestData("foo", new Dictionary<string, string>(), "foo", new string[0], null), "2.0");
            BuildRequestEntry requestEntry = new BuildRequestEntry(CreateNewBuildRequest(1, new string[] { "foo" }), config);
            TargetEntry entry = new TargetEntry(requestEntry, this, new TargetSpecification("Empty", null), null, null, _host, false);
        }

        /// <summary>
        /// Tests a constructor with a null host.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void TestConstructorNullHost()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            BuildRequestConfiguration config = new BuildRequestConfiguration(1, new BuildRequestData("foo", new Dictionary<string, string>(), "foo", new string[0], null), "2.0");
            BuildRequestEntry requestEntry = new BuildRequestEntry(CreateNewBuildRequest(1, new string[] { "foo" }), config);

            Lookup lookup = new Lookup(new ItemDictionary<ProjectItemInstance>(project.Items), new PropertyDictionary<ProjectPropertyInstance>(project.Properties), null);
            TargetEntry entry = new TargetEntry(requestEntry, this, new TargetSpecification("Empty", null), lookup, null, null, false);
        }

        /// <summary>
        /// Tests a valid constructor call.
        /// </summary>
        [TestMethod]
        public void TestConstructorValid()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            TargetEntry entry = CreateStandardTargetEntry(project, "Empty");
            Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
        }

        /// <summary>
        /// Tests incorrect invocation of ExecuteTarget
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InternalErrorException))]
        public void TestInvalidState_Execution()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            TargetEntry entry = CreateStandardTargetEntry(project, "Empty");
            Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
            ExecuteEntry(project, entry);
        }

        /// <summary>
        /// Tests incorrect invocation of GatherResults.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InternalErrorException))]
        public void TestInvalidState_Completed()
        {
            ProjectInstance project = CreateTestProject(true /* Returns enabled */);
            TargetEntry entry = CreateStandardTargetEntry(project, "Empty");
            Assert.AreEqual(entry.State, TargetEntryState.Dependencies);
            entry.GatherResults();
        }

        /// <summary>
        /// Verifies that the dependencies specified for a target are returned by the GetDependencies call.
        /// </summary>
        [TestMethod]
        public void TestDependencies()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                TargetEntry entry = CreateStandardTargetEntry(project, "Empty");

                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                Assert.AreEqual(0, deps.Count);

                entry = CreateStandardTargetEntry(project, "Baz");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                Assert.AreEqual(1, deps.Count);
                IEnumerator<TargetSpecification> depsEnum = deps.GetEnumerator();
                depsEnum.MoveNext();
                Assert.AreEqual("Bar", depsEnum.Current.TargetName);

                entry = CreateStandardTargetEntry(project, "Baz2");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                Assert.AreEqual(2, deps.Count);
                depsEnum = deps.GetEnumerator();
                depsEnum.MoveNext();
                Assert.AreEqual("Bar", depsEnum.Current.TargetName);
                depsEnum.MoveNext();
                Assert.AreEqual("Foo", depsEnum.Current.TargetName);
            }
        }

        /// <summary>
        /// Tests normal target execution and verifies the tasks expected to be executed are.
        /// </summary>
        [TestMethod]
        public void TestExecution()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                taskBuilder.Reset();
                TargetEntry entry = CreateStandardTargetEntry(project, "Empty");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                ExecuteEntry(project, entry);
                Assert.AreEqual(0, taskBuilder.ExecutedTasks.Count);

                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Baz");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                ExecuteEntry(project, entry);
                Assert.AreEqual(2, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("BazTask1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("BazTask2", taskBuilder.ExecutedTasks[1].Name);

                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Baz2");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                ExecuteEntry(project, entry);
                Assert.AreEqual(3, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("Baz2Task1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("Baz2Task2", taskBuilder.ExecutedTasks[1].Name);
                Assert.AreEqual("Baz2Task3", taskBuilder.ExecutedTasks[2].Name);
            }
        }

        /// <summary>
        /// Executes various cases where tasks cause an error.  Verifies that the expected tasks
        /// executed.
        /// </summary>
        [TestMethod]
        public void TestExecutionWithErrors()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                taskBuilder.Reset();
                TargetEntry entry = CreateStandardTargetEntry(project, "Error");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                taskBuilder.FailTaskNumber = 1;
                ExecuteEntry(project, entry);
                Assert.AreEqual(3, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("ErrorTask1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("ErrorTask2", taskBuilder.ExecutedTasks[1].Name);
                Assert.AreEqual("ErrorTask3", taskBuilder.ExecutedTasks[2].Name);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);

                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Error");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                taskBuilder.FailTaskNumber = 2;
                ExecuteEntry(project, entry);
                Assert.AreEqual(2, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("ErrorTask1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("ErrorTask2", taskBuilder.ExecutedTasks[1].Name);
                Assert.AreEqual(TargetEntryState.ErrorExecution, entry.State);
                Assert.AreEqual(2, entry.GetErrorTargets(GetProjectLoggingContext(entry.RequestEntry)).Count);

                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Error");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                taskBuilder.FailTaskNumber = 3;
                ExecuteEntry(project, entry);
                Assert.AreEqual(3, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("ErrorTask1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("ErrorTask2", taskBuilder.ExecutedTasks[1].Name);
                Assert.AreEqual("ErrorTask3", taskBuilder.ExecutedTasks[2].Name);
                Assert.AreEqual(TargetEntryState.ErrorExecution, entry.State);
                Assert.AreEqual(2, entry.GetErrorTargets(GetProjectLoggingContext(entry.RequestEntry)).Count);

                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Error");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                ExecuteEntry(project, entry);
                Assert.AreEqual(3, taskBuilder.ExecutedTasks.Count);
                Assert.AreEqual("ErrorTask1", taskBuilder.ExecutedTasks[0].Name);
                Assert.AreEqual("ErrorTask2", taskBuilder.ExecutedTasks[1].Name);
                Assert.AreEqual("ErrorTask3", taskBuilder.ExecutedTasks[2].Name);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
            }
        }

        /// <summary>
        /// Tests that the dependencies returned can also be built and that their entries in the lookup
        /// are appropriately aggregated into the parent target entry.
        /// </summary>
        [TestMethod]
        public void TestBuildDependencies()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                // Empty target doesn't produce any items of its own, the Compile items should be in it.
                TargetEntry entry = CreateStandardTargetEntry(project, "Baz2");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                foreach (TargetSpecification target in deps)
                {
                    TargetEntry depEntry = CreateStandardTargetEntry(project, target.TargetName, entry);
                    depEntry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                    ExecuteEntry(project, depEntry);
                    depEntry.GatherResults();
                }

                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                Assert.AreEqual(2, entry.Lookup.GetItems("Compile").Count);
                Assert.AreEqual(1, entry.Lookup.GetItems("FooTask1_Item").Count);
                Assert.AreEqual(1, entry.Lookup.GetItems("BarTask1_Item").Count);
            }
        }

        /// <summary>
        /// Tests a variety of situations returning various results
        /// </summary>
        [TestMethod]
        public void TestGatherResults()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                // Empty target doesn't produce any items of its own, the Compile items should be in it.
                // This target has no outputs.
                TargetEntry entry = CreateStandardTargetEntry(project, "Empty");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(2, entry.Lookup.GetItems("Compile").Count);
                Assert.AreEqual(0, results.Items.Length);
                Assert.AreEqual(TargetResultCode.Success, results.ResultCode);

                // Foo produces one item of its own and has an output
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Foo");
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                results = entry.GatherResults();
                Assert.AreEqual(2, entry.Lookup.GetItems("Compile").Count);
                Assert.AreEqual(1, entry.Lookup.GetItems("FooTask1_Item").Count);

                if (returnsEnabledForThisProject)
                {
                    // If returns are enabled, since this is a target with "Outputs", they won't 
                    // be returned. 
                    Assert.AreEqual(0, results.Items.Length);
                }
                else
                {
                    Assert.AreEqual(1, results.Items.Length);
                    Assert.AreEqual("foo.o", results.Items[0].ItemSpec);
                }

                Assert.AreEqual(TargetResultCode.Success, results.ResultCode);

                // Skip produces outputs but is up to date, so should record success
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Skip");
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                results = entry.GatherResults();

                if (returnsEnabledForThisProject)
                {
                    Assert.AreEqual(0, results.Items.Length);
                }
                else
                {
                    Assert.AreEqual(1, results.Items.Length);
                    Assert.AreEqual("testProject.proj", results.Items[0].ItemSpec);
                }

                Assert.AreEqual(TargetResultCode.Success, results.ResultCode);

                // SkipCondition is skipped due to condition.
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "SkipCondition");
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                results = entry.GatherResults();
                Assert.AreEqual(TargetResultCode.Skipped, results.ResultCode);

                // DepSkip produces no outputs and calls Empty and Skip.  The result should be success
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "DepSkip");
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                results = entry.GatherResults();
                Assert.AreEqual(TargetResultCode.Success, results.ResultCode);

                // DepSkip2 calls Skip.  The result should be success because both DepSkip and Skip are up-to-date.
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "DepSkip2");
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.Completed, entry.State);
                results = entry.GatherResults();
                Assert.AreEqual(TargetResultCode.Success, results.ResultCode);

                // Error target should produce error results
                taskBuilder.Reset();
                entry = CreateStandardTargetEntry(project, "Error");
                Assert.AreEqual(TargetEntryState.Dependencies, entry.State);
                deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                Assert.AreEqual(TargetEntryState.Execution, entry.State);
                taskBuilder.FailTaskNumber = 2;
                ExecuteEntry(project, entry);
                Assert.AreEqual(TargetEntryState.ErrorExecution, entry.State);
                entry.GetErrorTargets(GetProjectLoggingContext(entry.RequestEntry));
                results = entry.GatherResults();
                Assert.AreEqual(TargetResultCode.Failure, results.ResultCode);
            }
        }

        /// <summary>
        /// Tests that multiple outputs are allowed
        /// </summary>
        [TestMethod]
        public void TestMultipleOutputs()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "MultipleOutputsNoReturns");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();

                if (returnsEnabledForThisProject)
                {
                    // If returns are enabled, since this is a target with "Outputs", they won't 
                    // be returned. 
                    Assert.AreEqual(0, results.Items.Length);
                }
                else
                {
                    Assert.AreEqual(2, results.Items.Length);
                }
            }
        }

        /// <summary>
        /// Tests that multiple return values are still passed through, even when there is no Outputs specified.
        /// </summary>
        [TestMethod]
        public void TestMultipleReturnsNoOutputs()
        {
            ProjectInstance project = CreateTestProject(true /* returns are enabled */);
            MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

            TargetEntry entry = CreateStandardTargetEntry(project, "MultipleReturnsNoOutputs");
            ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
            ExecuteEntry(project, entry);
            TargetResult results = entry.GatherResults();

            Assert.AreEqual(3, results.Items.Length);
        }

        /// <summary>
        /// Tests that multiple return values are still passed through, and verifies that when both Outputs and Returns
        /// are specified, Returns is what controls the return value of the target.
        /// </summary>
        [TestMethod]
        public void TestMultipleReturnsWithOutputs()
        {
            ProjectInstance project = CreateTestProject(true /* returns are enabled */);
            MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

            TargetEntry entry = CreateStandardTargetEntry(project, "MultipleReturnsWithOutputs");
            ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
            ExecuteEntry(project, entry);
            TargetResult results = entry.GatherResults();

            Assert.AreEqual(3, results.Items.Length);
        }

        /// <summary>
        /// Tests that duplicate outputs are allowed
        /// </summary>
        [TestMethod]
        public void TestDuplicateOutputs()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "DupeOutputs");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(1, results.Items.Length);
            }
        }

        /// <summary>
        /// Tests that duplicate outputs are not trimmed under the false trim condition
        /// </summary>
        [TestMethod]
        public void TestKeepDuplicateOutputsTrue()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "DupeOutputsKeep");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(2, results.Items.Length);
            }
        }

        /// <summary>
        /// Tests that duplicate outputs are trimmed under the false keep condition
        /// </summary>
        [TestMethod]
        public void TestKeepDuplicateOutputsFalse()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "DupeOutputsNoKeep");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(1, results.Items.Length);
            }
        }

        /// <summary>
        /// Tests that duplicate outputs are trimmed if they have the same metadata
        /// </summary>
        [TestMethod]
        public void TestKeepDuplicateOutputsSameMetadata()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "DupeOutputsSameMetadata");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(1, results.Items.Length);
            }
        }

        /// <summary>
        /// Tests that duplicate outputs are not trimmed if they have different metadata
        /// </summary>
        [TestMethod]
        public void TestKeepDuplicateOutputsDiffMetadata()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                ProjectInstance project = CreateTestProject(returnsEnabledForThisProject);
                MockTaskBuilder taskBuilder = (MockTaskBuilder)_host.GetComponent(BuildComponentType.TaskBuilder);

                TargetEntry entry = CreateStandardTargetEntry(project, "DupeOutputsDiffMetadata");
                ICollection<TargetSpecification> deps = entry.GetDependencies(GetProjectLoggingContext(entry.RequestEntry));
                ExecuteEntry(project, entry);
                TargetResult results = entry.GatherResults();
                Assert.AreEqual(4, results.Items.Length);
            }
        }

        /// <summary>
        /// Tests that metadata references in target outputs are correctly expanded
        /// </summary>
        [TestMethod]
        public void TestMetadataReferenceInTargetOutputs()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            foreach (bool returnsEnabledForThisProject in returnsEnabled)
            {
                string content = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`http://schemas.microsoft.com/developer/msbuild/2003`>
    <ItemGroup>
        <SomeItem1 Include=`item1.cs`/>
        <SomeItem2 Include=`item2.cs`/>
    </ItemGroup>
    <Target Name=`a`>
        <CallTarget Targets=`b;c`>
            <Output TaskParameter=`TargetOutputs` PropertyName=`foo`/>
        </CallTarget>
        <Message Text=`[$(foo)]`/>
    </Target>
    <Target Name=`b` Outputs=`%(SomeItem1.Filename)`/>
    <Target Name=`c` " + (returnsEnabledForThisProject ? "Returns" : "Outputs") + @"=`%(SomeItem2.Filename)`/>
</Project>
                ";

                MockLogger log = Helpers.BuildProjectWithNewOMExpectSuccess(content);

                if (returnsEnabledForThisProject)
                {
                    log.AssertLogContains("item2");
                    log.AssertLogDoesntContain("item1;item2");
                }
                else
                {
                    log.AssertLogContains("item1;item2");
                }
            }
        }

        /// <summary>
        /// Tests that we get the target ouputs correctly.
        /// </summary>
        [TestMethod]
        public void TestTargetOutputsOnFinishedEvent()
        {
            bool[] returnsEnabled = new bool[] { false, true };

            string loggingVariable = Environment.GetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING");
            Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", "1");
            try
            {
                TargetLoggingContext.EnableTargetOutputLogging = true;

                foreach (bool returnsEnabledForThisProject in returnsEnabled)
                {
                    string content = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`http://schemas.microsoft.com/developer/msbuild/2003`>
    <ItemGroup>
        <SomeItem1 Include=`item1.cs`/>
        <SomeItem2 Include=`item2.cs`/>
    </ItemGroup>
    <Target Name=`a`>
        <CallTarget Targets=`b;c`>
            <Output TaskParameter=`TargetOutputs` PropertyName=`foo`/>
        </CallTarget>
        <Message Text=`[$(foo)]`/>
    </Target>
    <Target Name=`b` " + (returnsEnabledForThisProject ? "Returns" : "Outputs") + @"=`%(SomeItem1.Filename)`/>
    <Target Name=`c` Outputs=`%(SomeItem2.Filename)`/>
</Project>
                ";

                    // Only log critical event is false by default
                    MockLogger log = Helpers.BuildProjectWithNewOMExpectSuccess(content);

                    Assert.IsTrue(log.TargetFinishedEvents.Count == 3);

                    TargetFinishedEventArgs targeta = log.TargetFinishedEvents[2];
                    TargetFinishedEventArgs targetb = log.TargetFinishedEvents[0];
                    TargetFinishedEventArgs targetc = log.TargetFinishedEvents[1];

                    Assert.IsNotNull(targeta);
                    Assert.IsNotNull(targetb);
                    Assert.IsNotNull(targetc);

                    Assert.IsTrue(targeta.TargetName.Equals("a", StringComparison.OrdinalIgnoreCase));
                    Assert.IsTrue(targetb.TargetName.Equals("b", StringComparison.OrdinalIgnoreCase));
                    Assert.IsTrue(targetc.TargetName.Equals("c", StringComparison.OrdinalIgnoreCase));

                    IEnumerable targetOutputsA = targeta.TargetOutputs;
                    IEnumerable targetOutputsB = targetb.TargetOutputs;
                    IEnumerable targetOutputsC = targetc.TargetOutputs;

                    Assert.IsNull(targetOutputsA);
                    Assert.IsNotNull(targetOutputsB);

                    if (returnsEnabledForThisProject)
                    {
                        Assert.IsNull(targetOutputsC);
                    }
                    else
                    {
                        Assert.IsNotNull(targetOutputsC);
                    }

                    List<ITaskItem> outputListB = new List<ITaskItem>();
                    foreach (ITaskItem item in targetOutputsB)
                    {
                        outputListB.Add(item);
                    }

                    Assert.IsTrue(outputListB.Count == 1);
                    Assert.IsTrue(outputListB[0].ItemSpec.Equals("item1", StringComparison.OrdinalIgnoreCase));

                    if (!returnsEnabledForThisProject)
                    {
                        List<ITaskItem> outputListC = new List<ITaskItem>();
                        foreach (ITaskItem item in targetOutputsC)
                        {
                            outputListC.Add(item);
                        }

                        Assert.IsTrue(outputListC.Count == 1);

                        Assert.IsTrue(outputListC[0].ItemSpec.Equals("item2", StringComparison.OrdinalIgnoreCase));
                    }
                }
            }
            finally
            {
                TargetLoggingContext.EnableTargetOutputLogging = false;
                Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", loggingVariable);
            }
        }

        /// <summary>
        /// Tests that we get no target outputs when the environment variable is not set
        /// </summary>
        [TestMethod]
        public void TestTargetOutputsOnFinishedEventNoVariableSet()
        {
            bool[] returnsEnabled = new bool[] { true, false };

            string loggingVariable = Environment.GetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING");
            Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", null);
            try
            {
                foreach (bool returnsEnabledForThisProject in returnsEnabled)
                {
                    string content = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`http://schemas.microsoft.com/developer/msbuild/2003`>
    <ItemGroup>
        <SomeItem1 Include=`item1.cs`/>
        <SomeItem2 Include=`item2.cs`/>
    </ItemGroup>
    <Target Name=`a`>
        <CallTarget Targets=`b;c`>
            <Output TaskParameter=`TargetOutputs` PropertyName=`foo`/>
        </CallTarget>
        <Message Text=`[$(foo)]`/>
    </Target>
    <Target Name=`b` Outputs=`%(SomeItem1.Filename)`/>
    <Target Name=`c` " + (returnsEnabledForThisProject ? "Returns" : "Outputs") + @"=`%(SomeItem2.Filename)`/>
</Project>
                ";

                    // Only log critical event is false by default
                    MockLogger log = Helpers.BuildProjectWithNewOMExpectSuccess(content);

                    Assert.IsTrue(log.TargetFinishedEvents.Count == 3);

                    TargetFinishedEventArgs targeta = log.TargetFinishedEvents[2];
                    TargetFinishedEventArgs targetb = log.TargetFinishedEvents[0];
                    TargetFinishedEventArgs targetc = log.TargetFinishedEvents[1];

                    Assert.IsNotNull(targeta);
                    Assert.IsNotNull(targetb);
                    Assert.IsNotNull(targetc);

                    Assert.IsTrue(targeta.TargetName.Equals("a", StringComparison.OrdinalIgnoreCase));
                    Assert.IsTrue(targetb.TargetName.Equals("b", StringComparison.OrdinalIgnoreCase));
                    Assert.IsTrue(targetc.TargetName.Equals("c", StringComparison.OrdinalIgnoreCase));

                    IEnumerable targetOutputsA = targeta.TargetOutputs;
                    IEnumerable targetOutputsB = targetb.TargetOutputs;
                    IEnumerable targetOutputsC = targetc.TargetOutputs;

                    Assert.IsNull(targetOutputsA);
                    Assert.IsNull(targetOutputsB);
                    Assert.IsNull(targetOutputsC);
                }
            }
            finally
            {
                Environment.SetEnvironmentVariable("MSBUILDTARGETOUTPUTLOGGING", loggingVariable);
            }
        }

        /// <summary>
        /// Make sure that if an after target fails that the build result is reported as failed.
        /// </summary>
        [TestMethod]
        public void AfterTargetsShouldReportFailedBuild()
        {
            // Since we're creating our own BuildManager, we need to make sure that the default 
            // one has properly relinquished the inproc node
            NodeProviderInProc nodeProviderInProc = ((IBuildComponentHost)BuildManager.DefaultBuildManager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;
            if (nodeProviderInProc != null)
            {
                nodeProviderInProc.Dispose();
            }

            string content = @"
<Project ToolsVersion='msbuilddefaulttoolsversion' DefaultTargets='Build' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>
<Target Name='Build'>
 <Message Text='Hello'/>
</Target>

<Target Name='Boo' AfterTargets='build'>
  <Error Text='Hi in Boo'/>
</Target>
</Project>
                ";
            BuildManager manager = null;
            try
            {
                MockLogger logger = new MockLogger();
                List<ILogger> loggers = new List<ILogger>();
                loggers.Add(logger);

                ProjectCollection collection = new ProjectCollection();
                Project project = new Project(XmlReader.Create(new StringReader(content)), (IDictionary<string, string>)null, "4.0", collection);
                project.FullPath = FileUtilities.GetTemporaryFile();
                project.Save();
                File.Delete(project.FullPath);

                BuildParameters parameters = new BuildParameters(collection);
                parameters.Loggers = loggers;
                parameters.ShutdownInProcNodeOnBuildFinish = true;

                BuildRequestData data = new BuildRequestData(project.FullPath, new Dictionary<string, string>(), "4.0", new string[] { }, null);
                manager = new BuildManager();
                BuildResult result = manager.Build(parameters, data);

                // Make sure the overall result is failed
                Assert.AreEqual(BuildResultCode.Failure, result.OverallResult);

                // Expect the build target to pass
                Assert.IsTrue(result.ResultsByTarget["Build"].ResultCode == TargetResultCode.Success);
            }
            finally
            {
                // and we should clean up after ourselves, too. 
                if (manager != null)
                {
                    NodeProviderInProc inProcNodeProvider = ((IBuildComponentHost)manager).GetComponent(BuildComponentType.InProcNodeProvider) as NodeProviderInProc;

                    if (inProcNodeProvider != null)
                    {
                        inProcNodeProvider.Dispose();
                    }
                }
            }
        }

        /// <summary>
        /// Tests that with an invalid target specification (inputs but no outputs) we
        /// still raise the TargetFinished event.
        /// </summary>
        [TestMethod]
        public void TestTargetFinishedRaisedOnInvalidTarget()
        {
            string content = @"
<Project ToolsVersion=`msbuilddefaulttoolsversion` xmlns=`http://schemas.microsoft.com/developer/msbuild/2003`>
    <Target Name=`OnlyInputs` Inputs=`foo`>
        <Message Text=`This is an invalid target -- this text should never show.` />
    </Target>
</Project>
                ";

            // Only log critical event is false by default
            MockLogger log = Helpers.BuildProjectWithNewOMExpectFailure(content, allowTaskCrash: true);

            Assert.AreEqual(1, log.TargetFinishedEvents.Count);
        }

        #region ITargetBuilderCallback Members

        /// <summary>
        /// Empty impl
        /// </summary>
        Task<ITargetResult[]> ITargetBuilderCallback.LegacyCallTarget(string[] targets, bool continueOnError, ElementLocation referenceLocation)
        {
            throw new NotImplementedException();
        }

        #endregion

        #region IRequestBuilderCallback Members

        /// <summary>
        /// Empty impl
        /// </summary>
        Task<BuildResult[]> IRequestBuilderCallback.BuildProjects(string[] projectFiles, PropertyDictionary<ProjectPropertyInstance>[] properties, string[] toolsVersions, string[] targets, bool waitForResults)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Not implemented.
        /// </summary>
        Task IRequestBuilderCallback.BlockOnTargetInProgress(int blockingRequestId, string blockingTarget)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Empty impl
        /// </summary>
        void IRequestBuilderCallback.Yield()
        {
        }

        /// <summary>
        /// Empty impl
        /// </summary>
        void IRequestBuilderCallback.Reacquire()
        {
        }

        /// <summary>
        /// Empty impl
        /// </summary>
        void IRequestBuilderCallback.EnterMSBuildCallbackState()
        {
        }

        /// <summary>
        /// Empty impl
        /// </summary>
        void IRequestBuilderCallback.ExitMSBuildCallbackState()
        {
        }

        #endregion

        /// <summary>
        /// Executes the specified entry with the specified project.
        /// </summary>
        private void ExecuteEntry(ProjectInstance project, TargetEntry entry)
        {
            ITaskBuilder taskBuilder = _host.GetComponent(BuildComponentType.TaskBuilder) as ITaskBuilder;

            // GetAwaiter().GetResult() will flatten any AggregateException throw by the task.
            entry.ExecuteTarget(taskBuilder, entry.RequestEntry, GetProjectLoggingContext(entry.RequestEntry), CancellationToken.None).GetAwaiter().GetResult();
            ((IBuildComponent)taskBuilder).ShutdownComponent();
        }

        /// <summary>
        /// Creates a new build request
        /// </summary>
        private BuildRequest CreateNewBuildRequest(int configurationId, string[] targets)
        {
            return new BuildRequest(1 /* submissionId */, _nodeRequestId++, configurationId, targets, null, DefaultLicenseValidator.Invalid, null);
        }

        /// <summary>
        /// Creates a TargetEntry from a project and the specified target name.
        /// </summary>
        /// <param name="project">The project object.</param>
        /// <param name="targetName">The name of a target within the specified project.</param>
        /// <returns>The new target entry</returns>
        private TargetEntry CreateStandardTargetEntry(ProjectInstance project, string targetName)
        {
            BuildRequestConfiguration config = new BuildRequestConfiguration(1, new BuildRequestData("foo", new Dictionary<string, string>(), "foo", new string[0], null), "2.0");
            config.Project = project;
            BuildRequestEntry requestEntry = new BuildRequestEntry(CreateNewBuildRequest(1, new string[] { "foo" }), config);

            Lookup lookup = new Lookup(new ItemDictionary<ProjectItemInstance>(project.Items), new PropertyDictionary<ProjectPropertyInstance>(project.Properties), null);
            TargetEntry entry = new TargetEntry(requestEntry, this, new TargetSpecification(targetName, project.Targets[targetName].Location), lookup, null, _host, false);
            return entry;
        }

        /// <summary>
        /// Creates a target entry object.
        /// </summary>
        /// <param name="project">The project object</param>
        /// <param name="target">The target object</param>
        /// <param name="baseEntry">The parent entry.</param>
        /// <returns>The new target entry</returns>
        private TargetEntry CreateStandardTargetEntry(ProjectInstance project, string target, TargetEntry baseEntry)
        {
            BuildRequestConfiguration config = new BuildRequestConfiguration(1, new BuildRequestData("foo", new Dictionary<string, string>(), "foo", new string[0], null), "2.0");
            config.Project = project;
            BuildRequestEntry requestEntry = new BuildRequestEntry(CreateNewBuildRequest(1, new string[1] { "foo" }), config);
            TargetEntry entry = new TargetEntry(requestEntry, this, new TargetSpecification(target, project.Targets[target].Location), baseEntry.Lookup, baseEntry, _host, false);
            return entry;
        }

        /// <summary>
        /// Creates the test project
        /// </summary>
        /// <returns>The project object.</returns>
        private ProjectInstance CreateTestProject(bool returnsAttributeEnabled)
        {
            string returnsAttributeName = returnsAttributeEnabled ? "Returns" : "Outputs";

            string projectFileContents = @"
                <Project ToolsVersion='msbuilddefaulttoolsversion' xmlns='http://schemas.microsoft.com/developer/msbuild/2003'>

                    <ItemGroup>
                        <Compile Include='b.cs' />
                        <Compile Include='c.cs' />
                    </ItemGroup>

                    <ItemGroup>
                        <Reference Include='System' />
                    </ItemGroup>

                    <ItemGroup>
                        <DupeOutput Include='foo.cs'/>
                        <DupeOutput Include='foo.cs'/>
                    </ItemGroup>

                    <ItemGroup>
                        <DupeOutputDiffMetadata Include='foo.cs'>
                            <x>1</x>
                        </DupeOutputDiffMetadata>
                        <DupeOutputDiffMetadata Include='foo.cs'>
                            <x>2</x>
                        </DupeOutputDiffMetadata>
                        <DupeOutputDiffMetadata Include='foo.cs'>
                            <y>1</y>
                        </DupeOutputDiffMetadata>
                        <DupeOutputDiffMetadata Include='foo.cs'>
                            <y>2</y>
                        </DupeOutputDiffMetadata>
                    </ItemGroup>

                    <ItemGroup>
                        <DupeOutputsameMetadata Include='foo.cs'>
                            <x>1</x>
                        </DupeOutputsameMetadata>
                        <DupeOutputsameMetadata Include='foo.cs'>
                            <x>1</x>
                        </DupeOutputsameMetadata>
                    </ItemGroup>

                    <PropertyGroup>
                        <FalseProp>false</FalseProp>
                        <TrueProp>true</TrueProp>
                    </PropertyGroup>

                    <Target Name='Empty' />

                    <Target Name='Skip' Inputs='testProject.proj' Outputs='testProject.proj' />

                    <Target Name='SkipCondition' Condition=""'true' == 'false'"" />

                    <Target Name='Error' >
                        <ErrorTask1 ContinueOnError='True'/>                    
                        <ErrorTask2 ContinueOnError='False'/>  
                        <ErrorTask3 /> 
                        <OnError ExecuteTargets='Foo'/>                  
                        <OnError ExecuteTargets='Bar'/>                  
                    </Target>

                    <Target Name='Foo' Inputs='foo.cpp' Outputs='foo.o'>
                        <FooTask1/>
                    </Target>

                    <Target Name='Bar'>
                        <BarTask1/>
                    </Target>

                    <Target Name='Baz' DependsOnTargets='Bar'>
                        <BazTask1/>
                        <BazTask2/>
                    </Target>

                    <Target Name='Baz2' DependsOnTargets='Bar;Foo'>
                        <Baz2Task1/>
                        <Baz2Task2/>
                        <Baz2Task3/>
                    </Target>

                    <Target Name='DepSkip' DependsOnTargets='Skip'>
                        <DepSkipTask1/>
                        <DepSkipTask2/>
                        <DepSkipTask3/>
                    </Target>

                    <Target Name='DepSkip2' DependsOnTargets='Skip' Inputs='testProject.proj' Outputs='testProject.proj'>
                        <DepSkipTask1/>
                        <DepSkipTask2/>
                        <DepSkipTask3/>
                    </Target>

                    <Target Name='MultipleOutputsNoReturns' Inputs='testProject.proj' Outputs='@(Compile)'>
                    </Target>";

            if (returnsAttributeEnabled)
            {
                projectFileContents += @"
                    <Target Name='MultipleReturnsWithOutputs' Inputs='testProject.proj' Outputs='@(Compile)' Returns='@(Compile);@(Reference)' />

                    <Target Name='MultipleReturnsNoOutputs' Returns='@(Compile);@(Reference)' />";
            }

            projectFileContents += @"
                    <Target Name='DupeOutputs' " + returnsAttributeName + @"='@(DupeOutput)'>
                    </Target>

                    <Target Name='DupeOutputsKeep' KeepDuplicateOutputs='$(TrueProp)' " + returnsAttributeName + @"='@(DupeOutput)'>
                    </Target>

                    <Target Name='DupeOutputsNoKeep' KeepDuplicateOutputs='$(FalseProp)' " + returnsAttributeName + @"='@(DupeOutput)'>
                    </Target>

                    <Target Name='DupeOutputsSameMetadata' KeepDuplicateOutputs='$(FalseProp)' " + returnsAttributeName + @"='@(DupeOutputsameMetadata)'>
                    </Target>

                    <Target Name='DupeOutputsDiffMetadata' KeepDuplicateOutputs='$(FalseProp)' " + returnsAttributeName + @"='@(DupeOutputDiffMetadata)'>
                    </Target>
                </Project>
                ";

            FileStream stream = File.Create("testProject.proj");
            stream.Close();

            Project project = new Project(XmlReader.Create(new StringReader(projectFileContents)));
            return project.CreateProjectInstance();
        }

        /// <summary>
        /// Returns a project logging context.
        /// </summary>
        /// <param name="entry">The build request entry.</param>
        /// <returns>The project logging context.</returns>
        private ProjectLoggingContext GetProjectLoggingContext(BuildRequestEntry entry)
        {
            return new ProjectLoggingContext(new NodeLoggingContext(_host, 1, false), entry, null);
        }

        /// <summary>
        /// The mock component host.
        /// </summary>
        private class MockHost : MockLoggingService, IBuildComponentHost, IBuildComponent
        {
            #region IBuildComponentHost Members

            /// <summary>
            /// The configuration cache.
            /// </summary>
            private IConfigCache _configCache;

            /// <summary>
            /// The logging service.
            /// </summary>
            private ILoggingService _loggingService;

            /// <summary>
            /// The results cache.
            /// </summary>
            private IResultsCache _resultsCache;

            /// <summary>
            /// The request builder.
            /// </summary>
            private IRequestBuilder _requestBuilder;

            /// <summary>
            /// The mock task builder
            /// </summary>
            private ITaskBuilder _taskBuilder;

            /// <summary>
            /// The build parameters.
            /// </summary>
            private BuildParameters _buildParameters;

            /// <summary>
            /// Retrieves the LegacyThreadingData associated with a particular component host
            /// </summary>
            private LegacyThreadingData _legacyThreadingData;

            /// <summary>
            /// Constructor
            /// </summary>
            public MockHost()
            {
                _buildParameters = new BuildParameters();
                _legacyThreadingData = new LegacyThreadingData();

                _configCache = new ConfigCache();
                ((IBuildComponent)_configCache).InitializeComponent(this);

                _loggingService = this;

                _resultsCache = new ResultsCache();
                ((IBuildComponent)_resultsCache).InitializeComponent(this);

                _requestBuilder = new RequestBuilder();
                ((IBuildComponent)_requestBuilder).InitializeComponent(this);

                _taskBuilder = new MockTaskBuilder();
                ((IBuildComponent)_taskBuilder).InitializeComponent(this);
            }

            /// <summary>
            /// Gets the build-specific logging service.
            /// </summary>
            /// <returns>The logging service</returns>
            public ILoggingService LoggingService
            {
                get
                {
                    return _loggingService;
                }
            }

            /// <summary>
            /// Retrieves the LegacyThreadingData associated with a particular component host
            /// </summary>
            LegacyThreadingData IBuildComponentHost.LegacyThreadingData
            {
                get
                {
                    return _legacyThreadingData;
                }
            }

            /// <summary>
            /// Retrieves the name of the host.
            /// </summary>
            public string Name
            {
                get
                {
                    return "TargetEntry_Tests.MockHost";
                }
            }

            /// <summary>
            /// Gets the build parameters.
            /// </summary>
            public BuildParameters BuildParameters
            {
                get
                {
                    return _buildParameters;
                }
            }

            /// <summary>
            /// Gets the component of the specified type.
            /// </summary>
            /// <param name="type">The type of component to return.</param>
            /// <returns>The component</returns>
            public IBuildComponent GetComponent(BuildComponentType type)
            {
                switch (type)
                {
                    case BuildComponentType.ConfigCache:
                        return (IBuildComponent)_configCache;

                    case BuildComponentType.LoggingService:
                        return (IBuildComponent)_loggingService;

                    case BuildComponentType.ResultsCache:
                        return (IBuildComponent)_resultsCache;

                    case BuildComponentType.RequestBuilder:
                        return (IBuildComponent)_requestBuilder;

                    case BuildComponentType.TaskBuilder:
                        return (IBuildComponent)_taskBuilder;

                    default:
                        throw new ArgumentException("Unexpected type " + type);
                }
            }

            /// <summary>
            /// Register a component factory.
            /// </summary>
            public void RegisterFactory(BuildComponentType type, BuildComponentFactoryDelegate factory)
            {
            }

            #endregion

            #region IBuildComponent Members

            /// <summary>
            /// Sets the component host
            /// </summary>
            /// <param name="host">The component host</param>
            public void InitializeComponent(IBuildComponentHost host)
            {
                throw new NotImplementedException();
            }

            /// <summary>
            /// Shuts down the component
            /// </summary>
            public void ShutdownComponent()
            {
                throw new NotImplementedException();
            }

            #endregion
        }
    }
}
