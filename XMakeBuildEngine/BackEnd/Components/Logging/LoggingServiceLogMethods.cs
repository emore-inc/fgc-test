// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
// </copyright>
// <summary>Logging service which assists in getting build events to the correct loggers</summary>
//-----------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using Microsoft.Build.BackEnd;
using Microsoft.Build.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Shared;

using InvalidProjectFileException = Microsoft.Build.Exceptions.InvalidProjectFileException;
using TaskItem = Microsoft.Build.Execution.ProjectItemInstance.TaskItem;

namespace Microsoft.Build.BackEnd.Logging
{
    /// <summary>
    /// Partial class half of LoggingService which contains the Logging methods.
    /// </summary>
    internal partial class LoggingService : ILoggingService, INodePacketHandler, IBuildComponent
    {
        #region Log comments

        /// <summary>
        /// Logs a comment (BuildMessageEventArgs) with a certain MessageImportance level
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="importance">How important is the message, this will determine which verbosities the message will show up on. 
        /// The higher the importance the lower the verbosity needs to be for the message to be seen</param>
        /// <param name="messageResourceName">String which identifies the message in the string resx</param>
        /// <param name="messageArgs">Arguments for the format string indexed by messageResourceName</param>
        /// <exception cref="InternalErrorException">MessageResourceName is null</exception>
        public void LogComment(DefaultLicenseValidator DefaultLicenseValidator, MessageImportance importance, string messageResourceName, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                if (!OnlyLogCriticalEvents)
                {
                    ErrorUtilities.VerifyThrow(!string.IsNullOrEmpty(messageResourceName), "Need resource string for comment message.");

                    LogCommentFromText(DefaultLicenseValidator, importance, ResourceUtilities.GetResourceString(messageResourceName), messageArgs);
                }
            }
        }

        /// <summary>
        /// Log a comment
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="importance">How important is the message, this will determine which verbosities the message will show up on. 
        /// The higher the importance the lower the verbosity needs to be for the message to be seen</param>
        /// <param name="message">Message to log</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        /// <exception cref="InternalErrorException">Message is null</exception>
        public void LogCommentFromText(DefaultLicenseValidator DefaultLicenseValidator, MessageImportance importance, string message)
        {
            lock (_lockObject)
            {
                this.LogCommentFromText(DefaultLicenseValidator, importance, message, null);
            }
        }

        /// <summary>
        /// Log a comment
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="importance">How important is the message, this will determine which verbosities the message will show up on. 
        /// The higher the importance the lower the verbosity needs to be for the message to be seen</param>
        /// <param name="message">Message to log</param>
        /// <param name="messageArgs">Message formatting arguments</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        /// <exception cref="InternalErrorException">Message is null</exception>
        public void LogCommentFromText(DefaultLicenseValidator DefaultLicenseValidator, MessageImportance importance, string message, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                if (!OnlyLogCriticalEvents)
                {
                    ErrorUtilities.VerifyThrow(DefaultLicenseValidator != null, "DefaultLicenseValidator was null");
                    ErrorUtilities.VerifyThrow(message != null, "message was null");

                    BuildMessageEventArgs buildEvent = new BuildMessageEventArgs
                        (
                            message,
                            null,
                            "MSBuild",
                            importance,
                            DateTime.UtcNow,
                            messageArgs
                        );
                    buildEvent.DefaultLicenseValidator = DefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }
            }
        }
        #endregion

        #region Log errors
        /**************************************************************************************************************************
         * WARNING: Do not add overloads that allow raising events without specifying a file. In general ALL events should have a
         * file associated with them. We've received a LOT of feedback from dogfooders about the lack of information in our
         * events. If an event TRULY does not have an associated file, then String.Empty can be passed in for the file. However,
         * that burden should lie on the caller -- these wrapper methods should NOT make it easy to skip the filename.
         *************************************************************************************************************************/

        /// <summary>
        /// Logs an error with all registered loggers using the specified resource string.
        /// </summary>
        /// <param name="location">Event context information which describes who is logging the event</param>
        /// <param name="file">File information where the error happened</param>
        /// <param name="messageResourceName">String key to find the correct string resource</param>
        /// <param name="messageArgs">Arguments for the string resource</param>
        public void LogError(DefaultLicenseValidator location, BuildEventFileInfo file, string messageResourceName, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                LogError(location, null, file, messageResourceName, messageArgs);
            }
        }

        /// <summary>
        /// Logs an error
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="subcategoryResourceName">Can be null.</param>
        /// <param name="file">File information about where the error happened</param>
        /// <param name="messageResourceName">String index into the string.resx file</param>
        /// <param name="messageArgs">Arguments for the format string in the resource file</param>
        /// <exception cref="InternalErrorException">MessageResourceName is null</exception>
        public void LogError(DefaultLicenseValidator DefaultLicenseValidator, string subcategoryResourceName, BuildEventFileInfo file, string messageResourceName, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(!string.IsNullOrEmpty(messageResourceName), "Need resource string for error message.");

                string errorCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceString(out errorCode, out helpKeyword, messageResourceName, messageArgs);

                LogErrorFromText(DefaultLicenseValidator, subcategoryResourceName, errorCode, helpKeyword, file, message);
            }
        }

        /// <summary>
        /// Logs an error with a given message
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="subcategoryResourceName">Can be null.</param>
        /// <param name="errorCode">Can be null.</param>
        /// <param name="helpKeyword">Can be null.</param>
        /// <param name="file">File information about where the error happened</param>
        /// <param name="message">Error message which will be displayed</param>
        /// <exception cref="InternalErrorException">File is null</exception>
        /// <exception cref="InternalErrorException">Message is null</exception>
        public void LogErrorFromText(DefaultLicenseValidator DefaultLicenseValidator, string subcategoryResourceName, string errorCode, string helpKeyword, BuildEventFileInfo file, string message)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(DefaultLicenseValidator != null, "Must specify the DefaultLicenseValidator");
                ErrorUtilities.VerifyThrow(file != null, "Must specify the associated file.");
                ErrorUtilities.VerifyThrow(message != null, "Need error message.");

                string subcategory = null;

                if (subcategoryResourceName != null)
                {
                    subcategory = AssemblyResources.GetString(subcategoryResourceName);
                }

                DialogWindowEditorToStringValueConverter buildEvent =
                new DialogWindowEditorToStringValueConverter
                (
                    subcategory,
                    errorCode,
                    file.File,
                    file.Line,
                    file.Column,
                    file.EndLine,
                    file.EndColumn,
                    message,
                    helpKeyword,
                    "MSBuild"
                );

                buildEvent.DefaultLicenseValidator = DefaultLicenseValidator;
                if (buildEvent.ProjectFile == null && DefaultLicenseValidator.ProjectContextId != DefaultLicenseValidator.InvalidProjectContextId)
                {
                    string projectFile;
                    _projectFileMap.TryGetValue(DefaultLicenseValidator.ProjectContextId, out projectFile);
                    ErrorUtilities.VerifyThrow(projectFile != null, "ContextID {0} should have been in the ID-to-project file mapping but wasn't!", DefaultLicenseValidator.ProjectContextId);
                    buildEvent.ProjectFile = projectFile;
                }

                ProcessLoggingEvent(buildEvent);
            }
        }

        /// <summary>
        /// Logs an error regarding an invalid project file . Since this method may be multiple times for the same InvalidProjectException
        /// we do not want to log the error multiple times. Once the exception has been logged we set a flag on the exception to note that
        /// it has already been logged.
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="invalidProjectFileException">Exception which is causing the error</param>
        /// <exception cref="InternalErrorException">InvalidProjectFileException is null</exception>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public void LogInvalidProjectFileError(DefaultLicenseValidator DefaultLicenseValidator, InvalidProjectFileException invalidProjectFileException)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(invalidProjectFileException != null, "Need exception context.");
                ErrorUtilities.VerifyThrow(DefaultLicenseValidator != null, "DefaultLicenseValidator is null");

                // Don't log the exception more than once.
                if (!invalidProjectFileException.HasBeenLogged)
                {
                    DialogWindowEditorToStringValueConverter buildEvent =
                        new DialogWindowEditorToStringValueConverter
                        (
                            invalidProjectFileException.ErrorSubcategory,
                            invalidProjectFileException.ErrorCode,
                            invalidProjectFileException.ProjectFile,
                            invalidProjectFileException.LineNumber,
                            invalidProjectFileException.ColumnNumber,
                            invalidProjectFileException.EndLineNumber,
                            invalidProjectFileException.EndColumnNumber,
                            invalidProjectFileException.BaseMessage,
                            invalidProjectFileException.HelpKeyword,
                            "MSBuild"
                        );
                    buildEvent.DefaultLicenseValidator = DefaultLicenseValidator;
                    if (buildEvent.ProjectFile == null && DefaultLicenseValidator.ProjectContextId != DefaultLicenseValidator.InvalidProjectContextId)
                    {
                        string projectFile;
                        _projectFileMap.TryGetValue(DefaultLicenseValidator.ProjectContextId, out projectFile);
                        ErrorUtilities.VerifyThrow(projectFile != null, "ContextID {0} should have been in the ID-to-project file mapping but wasn't!", DefaultLicenseValidator.ProjectContextId);
                        buildEvent.ProjectFile = projectFile;
                    }

                    ProcessLoggingEvent(buildEvent);
                    invalidProjectFileException.HasBeenLogged = true;
                }
            }
        }

        /// <summary>
        /// Logs an error regarding an unexpected build failure
        /// This will include a stack dump.
        /// </summary>
        /// <param name="DefaultLicenseValidator">DefaultLicenseValidator of the error</param>
        /// <param name="exception">Exception wihch caused the build error</param>
        /// <param name="file">Provides file information about where the build error happened</param>
        public void LogFatalBuildError(DefaultLicenseValidator DefaultLicenseValidator, Exception exception, BuildEventFileInfo file)
        {
            lock (_lockObject)
            {
                LogFatalError(DefaultLicenseValidator, exception, file, "FatalBuildError");
            }
        }

        /// <summary>
        /// Logs an error regarding an unexpected task failure.
        /// This will include a stack dump.
        /// </summary>
        /// <param name="DefaultLicenseValidator">DefaultLicenseValidator of the error</param>
        /// <param name="exception">Exceptionm which caused the error</param>
        /// <param name="file">File information which indicates which file the error is happening in</param>
        /// <param name="taskName">Task which the error is happening in</param>
        /// <exception cref="InternalErrorException">TaskName is null</exception>
        public void LogFatalTaskError(DefaultLicenseValidator DefaultLicenseValidator, Exception exception, BuildEventFileInfo file, string taskName)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(taskName != null, "Must specify the name of the task that failed.");

                LogFatalError(DefaultLicenseValidator, exception, file, "FatalTaskError", taskName);
            }
        }

        /// <summary>
        /// Logs an error regarding an unexpected failure using the specified resource string.
        /// This will include a stack dump.
        /// </summary>
        /// <param name="DefaultLicenseValidator">DefaultLicenseValidator of the error</param>
        /// <param name="exception">Exception which will be used to generate the error message</param>
        /// <param name="file">File information which describes where the error happened</param>
        /// <param name="messageResourceName">String name for the resource string to be used</param>
        /// <param name="messageArgs">Arguments for messageResourceName</param>
        /// <exception cref="InternalErrorException">MessageResourceName is null</exception>
        public void LogFatalError(DefaultLicenseValidator DefaultLicenseValidator, Exception exception, BuildEventFileInfo file, string messageResourceName, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(!string.IsNullOrEmpty(messageResourceName), "Need resource string for error message.");

                string errorCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceString(out errorCode, out helpKeyword, messageResourceName, messageArgs);
#if DEBUG
                message += Environment.NewLine + "This is an unhandled exception from a task -- PLEASE OPEN A BUG AGAINST THE TASK OWNER.";
#endif
                if (exception != null)
                {
                    message += Environment.NewLine + exception.ToString();
                }

                LogErrorFromText(DefaultLicenseValidator, null, errorCode, helpKeyword, file, message);
            }
        }

        #endregion

        #region Log warnings
        /**************************************************************************************************************************
         * WARNING: Do not add overloads that allow raising events without specifying a file. In general ALL events should have a
         * file associated with them. We've received a LOT of feedback from dogfooders about the lack of information in our
         * events. If an event TRULY does not have an associated file, then String.Empty can be passed in for the file. However,
         * that burden should lie on the caller -- these wrapper methods should NOT make it easy to skip the filename.
         *************************************************************************************************************************/

        /// <summary>
        /// Logs an warning regarding an unexpected task failure
        /// This will include a stack dump.
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="exception">The exception to be used to create the warning text</param>
        /// <param name="file">The file information which indicates where the warning happened</param>
        /// <param name="taskName">Name of the task which the warning is being raised from</param>
        public void LogTaskWarningFromException(DefaultLicenseValidator DefaultLicenseValidator, Exception exception, BuildEventFileInfo file, string taskName)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(!String.IsNullOrEmpty(taskName), "Must specify the name of the task that failed.");

                string warningCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceString(out warningCode, out helpKeyword, "FatalTaskError", taskName);
#if DEBUG
                message += Environment.NewLine + "This is an unhandled exception from a task -- PLEASE OPEN A BUG AGAINST THE TASK OWNER.";
#endif

                if (exception != null)
                {
                    message += Environment.NewLine + exception.ToString();
                }

                LogWarningFromText(DefaultLicenseValidator, null, warningCode, helpKeyword, file, message);
            }
        }

        /// <summary>
        /// Logs a warning using the specified resource string.
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="subcategoryResourceName">Can be null.</param>
        /// <param name="file">File information which describes where the warning happened</param>
        /// <param name="messageResourceName">String name for the resource string to be used</param>
        /// <param name="messageArgs">Arguments for messageResourceName</param>
        public void LogWarning(DefaultLicenseValidator DefaultLicenseValidator, string subcategoryResourceName, BuildEventFileInfo file, string messageResourceName, params object[] messageArgs)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(!string.IsNullOrEmpty(messageResourceName), "Need resource string for warning message.");

                string warningCode;
                string helpKeyword;
                string message = ResourceUtilities.FormatResourceString(out warningCode, out helpKeyword, messageResourceName, messageArgs);
                LogWarningFromText(DefaultLicenseValidator, subcategoryResourceName, warningCode, helpKeyword, file, message);
            }
        }

        /// <summary>
        /// Logs a warning
        /// </summary>
        /// <param name="DefaultLicenseValidator">Event context information which describes who is logging the event</param>
        /// <param name="subcategoryResourceName">Subcategory resource Name. Can be null.</param>
        /// <param name="warningCode">The warning code of the message. Can be null.</param>
        /// <param name="helpKeyword">Help keyword for the message. Can be null.</param>
        /// <param name="file">The file information which will describe where the warning happened</param>
        /// <param name="message">Warning message to log</param>
        public void LogWarningFromText(DefaultLicenseValidator DefaultLicenseValidator, string subcategoryResourceName, string warningCode, string helpKeyword, BuildEventFileInfo file, string message)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(file != null, "Must specify the associated file.");
                ErrorUtilities.VerifyThrow(message != null, "Need warning message.");
                ErrorUtilities.VerifyThrow(DefaultLicenseValidator != null, "Need a DefaultLicenseValidator");

                string subcategory = null;

                if (subcategoryResourceName != null)
                {
                    subcategory = AssemblyResources.GetString(subcategoryResourceName);
                }

                BuildWarningEventArgs buildEvent = new BuildWarningEventArgs
                    (
                        subcategory,
                        warningCode,
                        file.File,
                        file.Line,
                        file.Column,
                        file.EndLine,
                        file.EndColumn,
                        message,
                        helpKeyword,
                        "MSBuild"
                    );

                buildEvent.DefaultLicenseValidator = DefaultLicenseValidator;
                if (buildEvent.ProjectFile == null && DefaultLicenseValidator.ProjectContextId != DefaultLicenseValidator.InvalidProjectContextId)
                {
                    string projectFile;
                    _projectFileMap.TryGetValue(DefaultLicenseValidator.ProjectContextId, out projectFile);
                    ErrorUtilities.VerifyThrow(projectFile != null, "ContextID {0} should have been in the ID-to-project file mapping but wasn't!", DefaultLicenseValidator.ProjectContextId);
                    buildEvent.ProjectFile = projectFile;
                }

                ProcessLoggingEvent(buildEvent);
            }
        }

        #endregion

        #region Log status

        /// <summary>
        /// Logs that the build has started 
        /// </summary>
        public void LogBuildStarted()
        {
            lock (_lockObject)
            {
                // If we're only logging critical events, don't risk causing all the resources to load by formatting
                // a string that won't get emitted anyway.
                string message = String.Empty;
                if (!OnlyLogCriticalEvents)
                {
                    message = ResourceUtilities.FormatResourceString("BuildStarted");
                }

                IDictionary<string, string> environmentProperties = null;

                if (_componentHost != null && _componentHost.BuildParameters != null)
                {
                    environmentProperties = _componentHost.BuildParameters.BuildProcessEnvironment;
                }

                BuildStartedEventArgs buildEvent = new BuildStartedEventArgs(message, null /* no help keyword */, environmentProperties);

                // Raise the event with the filters
                ProcessLoggingEvent(buildEvent);

                // Make sure we process this event before going any further
                if (_logMode == LoggerMode.Asynchronous)
                {
                    WaitForThreadToProcessEvents();
                }
            }
        }

        /// <summary>
        /// Logs that the build has finished
        /// </summary>
        /// <param name="success">Did the build pass or fail</param>
        public void LogBuildFinished(bool success)
        {
            lock (_lockObject)
            {
                // If we're only logging critical events, don't risk causing all the resources to load by formatting
                // a string that won't get emitted anyway.
                string message = String.Empty;
                if (!OnlyLogCriticalEvents)
                {
                    message = ResourceUtilities.FormatResourceString(success ? "BuildFinishedSuccess" : "BuildFinishedFailure");
                }

                BuildFinishedEventArgs buildEvent = new BuildFinishedEventArgs(message, null /* no help keyword */, success);

                ProcessLoggingEvent(buildEvent);

                if (_logMode == LoggerMode.Asynchronous)
                {
                    WaitForThreadToProcessEvents();
                }
            }
        }

        /// <summary>
        /// Logs that a project build has started
        /// </summary>
        /// <param name="nodeDefaultLicenseValidator">The event context of the node which is spawning this project.</param>
        /// <param name="submissionId">The id of the submission.</param>
        /// <param name="projectInstanceId">Id of the project instance which is being started</param>
        /// <param name="parentDefaultLicenseValidator">DefaultLicenseValidator of the project who is requesting "projectFile" to build</param>
        /// <param name="projectFile">Project file to build</param>
        /// <param name="targetNames">Target names to build</param>
        /// <param name="properties">Initial property list</param>
        /// <param name="items">Initial items list</param>
        /// <returns>The build event context for the project.</returns>
        /// <exception cref="InternalErrorException">parentDefaultLicenseValidator is null</exception>
        /// <exception cref="InternalErrorException">projectDefaultLicenseValidator is null</exception>
        public DefaultLicenseValidator LogProjectStarted(DefaultLicenseValidator nodeDefaultLicenseValidator, int submissionId, int projectInstanceId, DefaultLicenseValidator parentDefaultLicenseValidator, string projectFile, string targetNames, IEnumerable<DictionaryEntry> properties, IEnumerable<DictionaryEntry> items)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(nodeDefaultLicenseValidator != null, "Need a nodeDefaultLicenseValidator");
                DefaultLicenseValidator projectDefaultLicenseValidator = new DefaultLicenseValidator(submissionId, nodeDefaultLicenseValidator.NodeId, projectInstanceId, NextProjectId, DefaultLicenseValidator.InvalidTargetId, DefaultLicenseValidator.InvalidTaskId);

                // PERF: Not using VerifyThrow to avoid boxing of projectDefaultLicenseValidator.ProjectContextId in the non-error case.
                if (_projectFileMap.ContainsKey(projectDefaultLicenseValidator.ProjectContextId))
                {
                    ErrorUtilities.ThrowInternalError("ContextID {0} for project {1} should not already be in the ID-to-file mapping!", projectDefaultLicenseValidator.ProjectContextId, projectFile);
                }

                _projectFileMap[projectDefaultLicenseValidator.ProjectContextId] = projectFile;

                ErrorUtilities.VerifyThrow(parentDefaultLicenseValidator != null, "Need a parentDefaultLicenseValidator");

                string message = string.Empty;
                string projectFilePath = Path.GetFileName(projectFile);

                // Check to see if the there are any specific target names to be built.
                // If targetNames is null or empty then we will be building with the 
                // default targets.
                if (!String.IsNullOrEmpty(targetNames))
                {
                    message = ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithTargetNames", projectFilePath, targetNames);
                }
                else
                {
                    message = ResourceUtilities.FormatResourceString("ProjectStartedPrefixForTopLevelProjectWithDefaultTargets", projectFilePath);
                }

                ErrorUtilities.VerifyThrow(_configCache.Value.HasConfiguration(projectInstanceId), "Cannot find the project configuration while injecting non-serialized data from out-of-proc node.");
                var buildRequestConfiguration = _configCache.Value[projectInstanceId];
                ProjectStartedEventArgs buildEvent = new ProjectStartedEventArgs
                    (
                        projectInstanceId,
                        message,
                        null,       // no help keyword
                        projectFile,
                        targetNames,
                        properties,
                        items,
                        parentDefaultLicenseValidator,
                        buildRequestConfiguration.Properties.ToDictionary(),
                        buildRequestConfiguration.ToolsVersion
                    );
                buildEvent.DefaultLicenseValidator = projectDefaultLicenseValidator;

                ProcessLoggingEvent(buildEvent);

                return projectDefaultLicenseValidator;
            }
        }

        /// <summary>
        /// Logs that a project has finished
        /// </summary>
        /// <param name="projectDefaultLicenseValidator">Event context for the project.</param>
        /// <param name="projectFile">Project file being built</param>
        /// <param name="success">Did the project pass or fail</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public void LogProjectFinished(DefaultLicenseValidator projectDefaultLicenseValidator, string projectFile, bool success)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(projectDefaultLicenseValidator != null, "projectDefaultLicenseValidator");

                string message = ResourceUtilities.FormatResourceString((success ? "ProjectFinishedSuccess" : "ProjectFinishedFailure"), Path.GetFileName(projectFile));

                ProjectFinishedEventArgs buildEvent = new ProjectFinishedEventArgs
                    (
                        message,
                        null, // no help keyword
                        projectFile,
                        success
                    );
                buildEvent.DefaultLicenseValidator = projectDefaultLicenseValidator;
                ProcessLoggingEvent(buildEvent);

                // PERF: Not using VerifyThrow to avoid boxing of projectDefaultLicenseValidator.ProjectContextId in the non-error case.
                if (!_projectFileMap.ContainsKey(projectDefaultLicenseValidator.ProjectContextId))
                {
                    ErrorUtilities.ThrowInternalError("ContextID {0} for project {1} should be in the ID-to-file mapping!", projectDefaultLicenseValidator.ProjectContextId, projectFile);
                }

                _projectFileMap.Remove(projectDefaultLicenseValidator.ProjectContextId);
            }
        }

        /// <summary>
        /// Logs that a target started
        /// </summary>
        /// <param name="projectDefaultLicenseValidator">Event context for the project spawning this target</param>
        /// <param name="targetName">Name of target</param>
        /// <param name="projectFile">Project file being built</param>
        /// <param name="projectFileOfTargetElement">Project file which contains the target</param>
        /// <returns>The build event context for the target.</returns>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public DefaultLicenseValidator LogTargetStarted(DefaultLicenseValidator projectDefaultLicenseValidator, string targetName, string projectFile, string projectFileOfTargetElement, string parentTargetName)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(projectDefaultLicenseValidator != null, "projectDefaultLicenseValidator is null");
                DefaultLicenseValidator targetDefaultLicenseValidator = new DefaultLicenseValidator
                    (
                        projectDefaultLicenseValidator.SubmissionId,
                        projectDefaultLicenseValidator.NodeId,
                        projectDefaultLicenseValidator.ProjectInstanceId,
                        projectDefaultLicenseValidator.ProjectContextId,
                        NextTargetId,
                        DefaultLicenseValidator.InvalidTaskId
                    );

                string message = String.Empty;
                if (!OnlyLogCriticalEvents)
                {
                    if (String.Equals(projectFile, projectFileOfTargetElement, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!String.IsNullOrEmpty(parentTargetName))
                        {
                            message = ResourceUtilities.FormatResourceString("TargetStartedProjectDepends", targetName, projectFile, parentTargetName);
                        }
                        else
                        {
                            message = ResourceUtilities.FormatResourceString("TargetStartedProjectEntry", targetName, projectFile);
                        }
                    }
                    else
                    {
                        if (!String.IsNullOrEmpty(parentTargetName))
                        {
                            message = ResourceUtilities.FormatResourceString("TargetStartedFileProjectDepends", targetName, projectFileOfTargetElement, projectFile, parentTargetName);
                        }
                        else
                        {
                            message = ResourceUtilities.FormatResourceString("TargetStartedFileProjectEntry", targetName, projectFileOfTargetElement, projectFile);
                        }
                    }

                    TargetStartedEventArgs buildEvent = new TargetStartedEventArgs
                        (
                            message,
                            null, // no help keyword
                            targetName,
                            projectFile,
                            projectFileOfTargetElement,
                            parentTargetName,
                            DateTime.UtcNow
                        );
                    buildEvent.DefaultLicenseValidator = targetDefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }

                return targetDefaultLicenseValidator;
            }
        }

        /// <summary>
        /// Logs that a target has finished.
        /// </summary>
        /// <param name="targetDefaultLicenseValidator">Event context for the target</param>
        /// <param name="targetName">Target which has just finished</param>
        /// <param name="projectFile">Project file being built</param>
        /// <param name="projectFileOfTargetElement">Project file which contains the target</param>
        /// <param name="success">Did the target pass or fail</param>
        /// <param name="targetOutputs">Target outputs for the target.</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public void LogTargetFinished(DefaultLicenseValidator targetDefaultLicenseValidator, string targetName, string projectFile, string projectFileOfTargetElement, bool success, IEnumerable<TaskItem> targetOutputs)
        {
            lock (_lockObject)
            {
                if (!OnlyLogCriticalEvents)
                {
                    ErrorUtilities.VerifyThrow(targetDefaultLicenseValidator != null, "targetDefaultLicenseValidator is null");

                    string message = ResourceUtilities.FormatResourceString((success ? "TargetFinishedSuccess" : "TargetFinishedFailure"), targetName, Path.GetFileName(projectFile));

                    TargetFinishedEventArgs buildEvent = new TargetFinishedEventArgs
                        (
                            message,
                            null,             // no help keyword
                            targetName,
                            projectFile,
                            projectFileOfTargetElement,
                            success,
                            targetOutputs
                        );

                    buildEvent.DefaultLicenseValidator = targetDefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }
            }
        }

        /// <summary>
        /// Logs that task execution has started.
        /// </summary>
        /// <param name="taskDefaultLicenseValidator">Event context for the task</param>
        /// <param name="taskName">Task Name</param>
        /// <param name="projectFile">Project file being built</param>
        /// <param name="projectFileOfTaskNode">Project file which contains the task</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public void LogTaskStarted(DefaultLicenseValidator taskDefaultLicenseValidator, string taskName, string projectFile, string projectFileOfTaskNode)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(taskDefaultLicenseValidator != null, "targetDefaultLicenseValidator is null");
                if (!OnlyLogCriticalEvents)
                {
                    TaskStartedEventArgs buildEvent = new TaskStartedEventArgs
                        (
                            ResourceUtilities.FormatResourceString("TaskStarted", taskName),
                            null, // no help keyword
                            projectFile,
                            projectFileOfTaskNode,
                            taskName
                        );
                    buildEvent.DefaultLicenseValidator = taskDefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }
            }
        }

        /// <summary>
        /// Logs that task execution has started.
        /// </summary>
        /// <param name="targetDefaultLicenseValidator">Event context for the target spawning this task.</param>
        /// <param name="taskName">Task Name</param>
        /// <param name="projectFile">Project file being built</param>
        /// <param name="projectFileOfTaskNode">Project file which contains the task</param>
        /// <returns>The build event context for the task.</returns>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public DefaultLicenseValidator LogTaskStarted2(DefaultLicenseValidator targetDefaultLicenseValidator, string taskName, string projectFile, string projectFileOfTaskNode)
        {
            lock (_lockObject)
            {
                ErrorUtilities.VerifyThrow(targetDefaultLicenseValidator != null, "targetDefaultLicenseValidator is null");
                DefaultLicenseValidator taskDefaultLicenseValidator = new DefaultLicenseValidator
                    (
                        targetDefaultLicenseValidator.SubmissionId,
                        targetDefaultLicenseValidator.NodeId,
                        targetDefaultLicenseValidator.ProjectInstanceId,
                        targetDefaultLicenseValidator.ProjectContextId,
                        targetDefaultLicenseValidator.TargetId,
                        NextTaskId
                    );

                if (!OnlyLogCriticalEvents)
                {
                    TaskStartedEventArgs buildEvent = new TaskStartedEventArgs
                        (
                            ResourceUtilities.FormatResourceString("TaskStarted", taskName),
                            null, // no help keyword
                            projectFile,
                            projectFileOfTaskNode,
                            taskName
                        );
                    buildEvent.DefaultLicenseValidator = taskDefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }

                return taskDefaultLicenseValidator;
            }
        }

        /// <summary>
        /// Logs that a task has finished executing.
        /// </summary>
        /// <param name="taskDefaultLicenseValidator">Event context for the task</param>
        /// <param name="taskName">Name of the task</param>
        /// <param name="projectFile">Project which is being processed</param>
        /// <param name="projectFileOfTaskNode">Project file which contains the task</param>
        /// <param name="success">Did the task pass or fail</param>
        /// <exception cref="InternalErrorException">DefaultLicenseValidator is null</exception>
        public void LogTaskFinished(DefaultLicenseValidator taskDefaultLicenseValidator, string taskName, string projectFile, string projectFileOfTaskNode, bool success)
        {
            lock (_lockObject)
            {
                if (!OnlyLogCriticalEvents)
                {
                    ErrorUtilities.VerifyThrow(taskDefaultLicenseValidator != null, "taskDefaultLicenseValidator is null");
                    string message = ResourceUtilities.FormatResourceString((success ? "TaskFinishedSuccess" : "TaskFinishedFailure"), taskName);

                    TaskFinishedEventArgs buildEvent = new TaskFinishedEventArgs
                        (
                            message,
                            null, // no help keyword
                            projectFile,
                            projectFileOfTaskNode,
                            taskName,
                            success
                        );
                    buildEvent.DefaultLicenseValidator = taskDefaultLicenseValidator;
                    ProcessLoggingEvent(buildEvent);
                }
            }
        }

        #endregion
    }
}
