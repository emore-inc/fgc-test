﻿using System;
using System.Xml;
using System.Text;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Exceptions;
using Microsoft.Build.Execution;
using Microsoft.Build.Evaluation;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using System.IO;

namespace Microsoft.Build.UnitTests.QA
{
    /// <summary>
    /// Defines the build request for a project
    /// </summary>
    internal class RequestDefinition : IDisposable
    {
        #region Private Data members

        /// <summary>
        /// Build result generated of this definition
        /// </summary>
        private BuildResult buildResult;

        /// <summary>
        /// BuildRequest generated for this definition
        /// </summary>
        private BuildRequest buildRequest;

        /// <summary>
        /// List of child definitions which needs to be built before this definition
        /// </summary>
        private List<RequestDefinition> childDefinitions;

        /// <summary>
        /// Time in miliseconds this build needs to execute for
        /// </summary>
        private int executionTime;

        /// <summary>
        /// Targets to build
        /// </summary>
        private string[] targetsToBuild;

        /// <summary>
        /// Exception recorded during a build process
        /// </summary>
        private Exception buildException;

        /// <summary>
        /// The test data provider
        /// </summary>
        private ITestDataProvider testDataProvider;

        /// <summary>
        /// The BuildRequsetEngine
        /// </summary>
        private IBuildRequestEngine requestEngine;

        /// <summary>
        /// The BuildRequsetEngine
        /// </summary>
        private IResultsCache resultsCache;

        /// <summary>
        /// File name associated to this definition
        /// </summary>
        private string fileName;

        /// <summary>
        /// Tools version associated to this definition
        /// </summary>
        private string toolsVersion;

        /// <summary>
        /// Global properties associated to this definition
        /// </summary>
        private PropertyDictionary<ProjectPropertyInstance> globalProperties;

        /// <summary>
        /// BuildRequestConfiguration associated with this definition
        /// </summary>
        private BuildRequestConfiguration configuration;

        /// <summary>
        /// Event that gets fired when the request has been completed
        /// </summary>
        private AutoResetEvent testProjectCompletedEvent;

        /// <summary>
        /// Elements defining the actual project
        /// </summary>
        private ProjectDefinition projectDefinition;

        /// <summary>
        /// This request is used for testing cancels
        /// </summary>
        private bool waitForCancel;

        /// <summary>
        /// Global request id starts at 2. 1 is reserved for the root
        /// </summary>
        private static int globalRequestId = 2;

        /// <summary>
        /// Default tools version name
        /// </summary>
        public const string defaultToolsVersion = "defaulttoolsversion";

        /// <summary>
        /// Default target to build name
        /// </summary>
        public const string defaultTargetName = "defaulttarget1";

        /// <summary>
        /// Default taskName
        /// </summary>
        public const string defaultTaskName = "defaulttask1";

        #endregion

        #region Constructor

        /// <summary>
        /// Constructor takes in the filename.
        /// </summary>
        public RequestDefinition(string fileName, IBuildComponentHost host)
            : this(fileName, null, null, null, 0, null, host)
        {
        }

        /// <summary>
        /// Constructor takes in the filename and the noTargetToBuild parameters.
        /// </summary>
        public RequestDefinition(string fileName, IBuildComponentHost host, bool noTargetsToBuild)
            : this(fileName, null, null, null, 0, null, host, noTargetsToBuild)
        {
        }

        /// <summary>
        /// Constructor takes in the filename and an array of child definitions.
        /// </summary>
        public RequestDefinition(string fileName, RequestDefinition[] childDefinitions, IBuildComponentHost host)
            : this(fileName, null, null, null, 0, childDefinitions, host)
        {
        }

        /// <summary>
        /// Constructor takes the filename and the build execution time
        /// </summary>
        public RequestDefinition(string fileName, int executionTime, IBuildComponentHost host)
            : this(fileName, null, null, null, executionTime, null, host)
        {
        }

        /// <summary>
        /// Constructor which sets most of the data members except for the noTargetsToBuild bool parameter
        /// </summary>
        public RequestDefinition(string fileName, string toolsVersion, string[] targets, PropertyDictionary<ProjectPropertyInstance> properties, int executionTime, RequestDefinition[] childDefinitions, IBuildComponentHost host)
            : this(fileName, toolsVersion, targets, properties, executionTime, childDefinitions, host, false)
        {
        }
        /// <summary>
        /// Constructor allows you to set the filname, toolsversion, targets to build, build properties and execution time.
        /// Following are the defaults:
        /// ToolsVersion = "ToolsVersion"
        /// GlobalProperties = new BuildPropertyGroup()
        /// ExecutionTime = 0;
        /// Targets to build = "target1"
        /// </summary>
        public RequestDefinition(string fileName, string toolsVersion, string[] targets, PropertyDictionary<ProjectPropertyInstance> properties, int executionTime, RequestDefinition[] childDefinitions, IBuildComponentHost host, bool noTargetsToBuild)
        {
            if (noTargetsToBuild || targets == null)
            {
                this.targetsToBuild = new string[] { };
            }
            else
            {
                this.targetsToBuild = targets;
            }

            this.globalProperties = ((properties == null) ? new PropertyDictionary<ProjectPropertyInstance>() : properties);
            this.toolsVersion = ((toolsVersion == null) ? RequestDefinition.defaultToolsVersion : toolsVersion);
            this.fileName = fileName;
            if (childDefinitions != null)
            {
                this.childDefinitions = new List<RequestDefinition>(childDefinitions);
                foreach (RequestDefinition bd in childDefinitions)
                {
                    this.childDefinitions.Add(bd);
                }
            }
            else
            {
                this.childDefinitions = new List<RequestDefinition>();
            }

            this.testProjectCompletedEvent = new AutoResetEvent(false);
            this.executionTime = executionTime;
            this.requestEngine = (IBuildRequestEngine)host.GetComponent(BuildComponentType.RequestEngine);
            this.testDataProvider = (ITestDataProvider)host.GetComponent(BuildComponentType.TestDataProvider);
            this.resultsCache = (IResultsCache)host.GetComponent(BuildComponentType.ResultsCache);
            this.testDataProvider.AddDefinition(this);
            this.projectDefinition = new ProjectDefinition(this.fileName);
            this.waitForCancel = false;
            
        }

        #endregion

        #region Events handlers raised by the ITestDataProvider

        /// <summary>
        /// New configuration request. New BuildRequestConfiguration is created and added to the configuration cache.
        /// This call is in the ProcessorThread of the TestDataProvider
        /// </summary>
        public void RaiseOnNewConfigurationRequest(BuildRequestConfiguration config)
        {
            if (this.configuration != null)
            {
                string message = String.Format("Configuration for request {0}:{1} has already been created", this.configuration.ConfigurationId, this.configuration.ProjectFullPath);
                throw new InvalidOperationException(message);
            }

            this.configuration = this.testDataProvider.CreateConfiguration(this);
            this.requestEngine.ReportConfigurationResponse(new BuildRequestConfigurationResponse(config.ConfigurationId, this.configuration.ConfigurationId, this.configuration.ResultsNodeId));
        }

        /// <summary>
        /// A new build request for this definition is submitted. This call is in the ProcessorThread of the TestDataProvider
        /// </summary>
        public void RaiseOnNewBuildRequest(BuildRequest request)
        {
            this.Build(request);
        }

        /// <summary>
        /// Request for this definition is completed. This call is in the ProcessorThread of the TestDataProvider
        /// </summary>
        public void RaiseOnBuildRequestCompleted(BuildRequest request, BuildResult result)
        {
            if (!result.ResultBelongsToRootRequest)
            {
                // Don't report root requests to the request engine, as it doesn't wait on them.
                this.requestEngine.UnblockBuildRequest(new BuildRequestUnblocker(result));
            }
            
            this.buildResult = result;
            this.testProjectCompletedEvent.Set();
        }

        /// <summary>
        /// Build request engine threw an exception. This call is in the ProcessorThread of the TestDataProvider
        /// </summary>
        public void RaiseEngineException(Exception e)
        {
            this.buildException = e;
            this.testProjectCompletedEvent.Set();
        }

        #endregion

        #region IDisposable Members

        /// <summary>
        /// Clean list and events
        /// </summary>
        public void Dispose()
        {
            this.testProjectCompletedEvent.Close();
            this.childDefinitions.Clear();
            GC.SuppressFinalize(this);
        }

        #endregion

        #region Public Properties

        /// <summary>
        /// Returns an unresolved configuration for this object
        /// </summary>
        public BuildRequestConfiguration UnresolvedConfiguration
        {
            get
            {
                return new BuildRequestConfiguration(new BuildRequestData(this.fileName, this.globalProperties.ToDictionary(), this.toolsVersion, new string[0], null), "2.0");
            }
        }

        /// <summary>
        /// Returns the resolved BuildRequestConfiguration
        /// </summary>
        public BuildRequestConfiguration Configuration
        {
            get
            {
                return this.configuration;
            }
        }


        /// <summary>
        /// Targets to build for the project
        /// </summary>
        public string[] TargetsToBuild
        {
            get
            {
                return this.targetsToBuild;
            }
        }

        /// <summary>
        /// Simulate execution of a task by sleeping for the provided # of milliseconds
        /// </summary>
        public int ExecutionTime
        {
            get
            {
                return this.executionTime;
            }
        }



        /// <summary>
        /// Exception throw by the engine during the build process
        /// </summary>
        public Exception BuildException
        {
            get
            {
                return this.buildException;
            }
        }


        /// <summary>
        /// Referenes of this project
        /// </summary>
        public List<RequestDefinition> ChildDefinitions
        {
            get
            {
                return this.childDefinitions;
            }
        }


        /// <summary>
        /// Build result
        /// </summary>
        public BuildResult Result
        {
            get
            {
                return this.buildResult;
            }
        }

        /// <summary>
        /// File name of the project file
        /// </summary>
        public string FileName
        {
            get
            {
                return this.fileName;
            }
        }

        /// <summary>
        /// Tools version to be used when building the project
        /// </summary>
        public string ToolsVersion
        {
            get
            {
                return this.toolsVersion;
            }
        }

        /// <summary>
        /// Build properties to send when building the projects
        /// </summary>
        public PropertyDictionary<ProjectPropertyInstance> GlobalProperties
        {
            get
            {
                return this.globalProperties;
            }
        }

        /// <summary>
        /// The project definition
        /// </summary>
        public ProjectDefinition ProjectDefinition
        {
            get
            {
                return this.projectDefinition;
            }
            set
            {
                this.projectDefinition = value;
            }
        }

        /// <summary>
        /// If MSBuild project object is to be created
        /// </summary>
        public bool CreateMSBuildProject
        {
            get
            {
                return this.projectDefinition.CreateMSBuildProject;
            }
            set
            {
                this.projectDefinition.CreateMSBuildProject = value;
            }
        }

        /// <summary>
        /// This request is used for testing cancels
        /// </summary>
        public bool WaitForCancel
        {
            get
            {
                return this.waitForCancel;
            }
            set
            {
                this.waitForCancel = true;
            }
        }

        #endregion

        #region Public Methods

        /// <summary>
        /// Adds a reference project to the listw
        /// </summary>
        public void AddChildDefinition(RequestDefinition project)
        {
            this.childDefinitions.Add(project);
        }

        /// <summary>
        /// Checks if the result received for this build definition is valid. If nothing happens in 10 seconds
        /// then we will terminate. This call is in the thread where the test is running. This also validates if
        /// the targets requested to be built is built correctly
        /// </summary>
        public void ValidateBuildResult()
        {
            WaitForResults();
            int targetCount = 0;

            // No specific target was specified to build. Since validateBuildResult is only called for tests where the
            // request builder and there on is mocked - just testing request engine - the end result will be a default target
            if (this.targetsToBuild.Length == 0)
            {
                Assert.IsTrue(this.buildResult.HasResultsForTarget(RequestDefinition.defaultTargetName), "Should have results for target:" + RequestDefinition.defaultTargetName);
            }
            else
            {
                foreach (string target in this.targetsToBuild)
                {
                    targetCount++;
                    Assert.IsTrue(this.buildResult.HasResultsForTarget(target), "Should have results for target:" + target);
                }

                Assert.AreEqual(targetCount, this.targetsToBuild.Length, "Total target count and returned target count does not match");
            }

            Assert.IsTrue(this.buildResult.OverallResult == BuildResultCode.Success, "Overall results should be a success");
        }

        /// <summary>
        /// Validate if a Build Aborted exception was received by the host from the engine
        /// </summary>
        public void ValidateBuildAbortedResult()
        {
            WaitForResultsDontFail();

            Assert.IsTrue(this.Result.Exception != null, "Expected to receive a Build Aborted exception but no exception was recorded");
            BuildAbortedException be = this.Result.Exception as BuildAbortedException;
            Assert.IsTrue(be != null, "Expected to receive a BuildAbortedException but received: " + this.Result.Exception.Message);
        }


        /// <summary>
        /// Waits for the build request to complete for this this.Result.Exceptionest definition. If nothing happens in 10 seconds
        /// then we will terminate. This call is in the thread where the test is running. This does not verify if the targets
        /// were built successfully. An exception is thrown if any of the exception object is populated in the result
        /// </summary>
        public void WaitForResultsThrowException()
        {
            WaitForResultsInternal(true, false);
        }

        /// <summary>
        /// Waits for the build request to complete for this test definition. If nothing happens in 10 seconds
        /// then we will terminate. This call is in the thread where the test is running. This does not verify if the targets
        /// were built successfully. An exception is not thrown if any of the exception object is populated in the result but we do
        /// Assert.Fail
        /// </summary>
        public void WaitForResults()
        {
            WaitForResultsInternal(false, false);
        }

        /// <summary>
        /// /// <summary>
        /// Waits for the build request to complete for this test definition. If nothing happens in 10 seconds
        /// then we will terminate. This call is in the thread where the test is running. This does not verify if the targets
        /// were built successfully. An exception is not thrown if any of the exception object is populated in the result and we do not do any
        /// Assert.Fail either
        /// </summary>
        /// </summary>
        public void WaitForResultsDontFail()
        {
            WaitForResultsInternal(false, true);
        }

        /// <summary>
        /// Waits for the build request to complete for this test definition. If nothing happens in 10 seconds
        /// then we will terminate. This call is in the thread where the test is running. This does not verify if the targets
        /// were built successfully
        /// </summary>
        private void WaitForResultsInternal(bool throwOnException, bool dontFail)
        {
            bool signaled = this.testProjectCompletedEvent.WaitOne(QAMockHost.globalTimeOut, false);

            if (!signaled)
            {
                Assert.Fail("Timeout after- " + QAMockHost.globalTimeOut.ToString() + " seconds waiting for project:" + this.fileName + " to complete");
            }
            else
            {
                if (this.buildException != null)
                {
                    Assert.Fail("Received engine exception: " + this.buildException + " when building project:" + this.fileName);
                }
                else
                {
                    Assert.IsTrue(this.buildResult.ConfigurationId == this.configuration.ConfigurationId);
                    if (this.buildResult.Exception != null)
                    {
                        if (throwOnException)
                        {
                            throw this.buildResult.Exception;
                        }
                        
                        if(!dontFail)
                        {
                            Assert.Fail(this.buildResult.Exception.Message);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Validates the result for a particular target
        /// </summary>
        public void ValidateTargetBuilt(string targetName)
        {
            Assert.IsTrue(this.buildResult.HasResultsForTarget(targetName), "Should have results for target:" + targetName);
        }

        /// <summary>
        /// Validates the result for a particular target
        /// </summary>
        public void ValidateTargetDidNotBuild(string targetName)
        {
            Assert.IsFalse(this.buildResult.HasResultsForTarget(targetName), "Should have not have results for target:" + targetName);
            
        }

        /// <summary>
        /// Checks if the target results are as expected. This method can only be called for targets may be built as a result of building
        /// the requested / default / initial targets
        /// </summary>
        public void ValidateNonPrimaryTargetEndResult(string targetName, TargetResultCode expectedResultCode, string[] items)
        {
            BuildResult result = this.resultsCache.GetResultForRequest(this.buildRequest);
            TargetResult targetResult = (TargetResult)result[targetName];
            InternalValidateTargetEndResult(targetResult, expectedResultCode, items);
        }

        /// <summary>
        /// Checks if the target results are as expected. This method can only be called for targets which were specifically requested
        /// to be built or the default/initial targets
        /// </summary>
        public void ValidateTargetEndResult(string targetName, TargetResultCode expectedResultCode, string[] items)
        {
            TargetResult targetResult = (TargetResult)this.buildResult[targetName];
            InternalValidateTargetEndResult(targetResult, expectedResultCode, items);
        }

        /// <summary>
        /// Checks if the target results are as expected.
        /// </summary>
        private void InternalValidateTargetEndResult(TargetResult targetResult, TargetResultCode expectedResultCode, string[] items)
        {
            int foundCount = 0;

            Assert.AreEqual(expectedResultCode, targetResult.ResultCode, "Expected result is not the same as the received result");
            if (items != null)
            {
                foreach (string item in items)
                {
                    bool foundItemValue = false;

                    foreach (ITaskItem i in targetResult.Items)
                    {
                        if (item == i.ItemSpec)
                        {
                            foundItemValue = true;
                            foundCount++;
                            break;
                        }
                    }

                    Assert.IsTrue(foundItemValue, "Item not found in result");
                }

                Assert.IsTrue(foundCount == items.Length, "Total items expected was not the same as what was received.");
            }
        }

        /// <summary>
        /// Have we revceived a result for this request yet. If we have already been signaled, since this is a auto reset event, the state will
        /// transit to not signaled and thus ValidateResults will fail. So we have to signal it again.
        /// </summary>
        public bool IsResultAvailable()
        {
            bool signaled = this.testProjectCompletedEvent.WaitOne(1, false);
            if (signaled)
            {
                this.testProjectCompletedEvent.Set();
            }

            return signaled;
        }

        /// <summary>
        /// Cache the configuration for this build definition and then submit the build request to the request engine. If the request being submitted does not have
        /// a GlobalRequestId than assign one.
        /// Since we want to make all requests rooted - it the passed in request is null then we will use the dummy root request and make that the parent. This is usually
        /// the case when tests submit build requets. When a request is submitted by the RequestBuilder the request is always populated and likely rooted.
        /// </summary>
        public void Build(BuildRequest request)
        {
            if (request == null)
            {
                this.configuration = testDataProvider.CreateConfiguration(this);
                this.buildRequest = new BuildRequest(1 /* submissionId */, 1, this.configuration.ConfigurationId, this.targetsToBuild, null, DefaultLicenseValidator.Invalid, null);
                this.buildRequest.GlobalRequestId = RequestDefinition.globalRequestId++;
            }
            else
            {
                this.buildRequest = request;
                // Assign a new Global Request id if one is not already there
                bool assignNewId = false;
                if (this.buildRequest.GlobalRequestId == BuildRequest.InvalidGlobalRequestId)
                {
                    foreach (KeyValuePair<int, RequestDefinition> idRequestPair in this.testDataProvider.RequestDefinitions)
                    {
                        if (
                            idRequestPair.Value.buildRequest != null &&
                            idRequestPair.Value.buildRequest.ConfigurationId == this.buildRequest.ConfigurationId &&
                            idRequestPair.Value.buildRequest.Targets.Count == this.buildRequest.Targets.Count
                            )
                        {
                            List<string> leftTargets = new List<string>(idRequestPair.Value.buildRequest.Targets);
                            List<string> rightTargets = new List<string>(this.buildRequest.Targets);
                            leftTargets.Sort(StringComparer.OrdinalIgnoreCase);
                            rightTargets.Sort(StringComparer.OrdinalIgnoreCase);
                            for (int i = 0; i < leftTargets.Count; i++)
                            {
                                if (!leftTargets[i].Equals(rightTargets[i], StringComparison.OrdinalIgnoreCase))
                                {
                                    assignNewId = true;
                                    break;
                                }
                            }

                            if (!assignNewId)
                            {
                                this.buildRequest.GlobalRequestId = idRequestPair.Value.buildRequest.GlobalRequestId;
                                break;
                            }
                        }
                    }
                }

                if (assignNewId)
                {
                    this.buildRequest.GlobalRequestId = RequestDefinition.globalRequestId++;
                }
            }

            this.requestEngine.SubmitBuildRequest(this.buildRequest);
        }

        /// <summary>
        /// Cache the configuration for this build definition and then submit the build request to the request engine
        /// </summary>
        public void SubmitBuildRequest()
        {
            Build(null);
        }


        /// <summary>
        /// Checks if the passed un-resolved configuration is the same as this definition's configuration elements. If the global properties are not present on either
        /// then just compare the file name and the tools version
        /// </summary>
        public bool AreSameDefinitions(BuildRequestConfiguration config)
        {
            if (this.globalProperties.Count == 0 && ((PropertyDictionary<ProjectPropertyInstance>)(config.Properties)).Count == 0)
            {
                if (
                    String.Compare(this.fileName, config.ProjectFullPath, StringComparison.OrdinalIgnoreCase) == 0 &&
                    String.Compare(this.toolsVersion, config.ToolsVersion, StringComparison.OrdinalIgnoreCase) == 0
                    )
                {
                    return true;
                }
            }
            else
            {
                if (
                    String.Compare(this.fileName, config.ProjectFullPath, StringComparison.OrdinalIgnoreCase) == 0 &&
                    String.Compare(this.toolsVersion, config.ToolsVersion, StringComparison.OrdinalIgnoreCase) == 0 &&
                    this.globalProperties.Equals((PropertyDictionary<ProjectPropertyInstance>)(config.Properties))
                   )
                {
                    return true;
                }
            }
            return false;
        }

        #endregion
    }
}
