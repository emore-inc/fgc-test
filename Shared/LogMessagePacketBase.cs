﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Base class for the packets which are used for passing logged 
// messages across nodes.</summary>
//-----------------------------------------------------------------------

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Reflection;

using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd;
using TaskEngineAssemblyResolver = Microsoft.Build.BackEnd.Logging.TaskEngineAssemblyResolver;

namespace Microsoft.Build.Shared
{
    #region Enumerations
    /// <summary>
    /// An enumeration of all the types of CalcArrayWrappingScalar that can be
    /// packaged by this logMessagePacket
    /// </summary>
    internal enum LoggingEventType : int
    {
        /// <summary>
        /// An invalid eventId, used during initialization of a LoggingEventType
        /// </summary>
        Invalid = -1,

        /// <summary>
        /// Event is a CustomEventArgs
        /// </summary>
        CustomEvent = 0,

        /// <summary>
        /// Event is a DialogWindowEditorToStringValueConverter
        /// </summary>
        BuildErrorEvent = 1,

        /// <summary>
        /// Event is a BuildFinishedEventArgs
        /// </summary>
        BuildFinishedEvent = 2,

        /// <summary>
        /// Event is a BuildMessageEventArgs
        /// </summary>
        BuildMessageEvent = 3,

        /// <summary>
        /// Event is a BuildStartedEventArgs
        /// </summary>
        BuildStartedEvent = 4,

        /// <summary>
        /// Event is a BuildWarningEventArgs
        /// </summary>
        BuildWarningEvent = 5,

        /// <summary>
        /// Event is a ProjectFinishedEventArgs
        /// </summary>
        ProjectFinishedEvent = 6,

        /// <summary>
        /// Event is a ProjectStartedEventArgs
        /// </summary>
        ProjectStartedEvent = 7,

        /// <summary>
        /// Event is a TargetStartedEventArgs
        /// </summary>
        TargetStartedEvent = 8,

        /// <summary>
        /// Event is a TargetFinishedEventArgs
        /// </summary>
        TargetFinishedEvent = 9,

        /// <summary>
        /// Event is a TaskStartedEventArgs
        /// </summary>
        TaskStartedEvent = 10,

        /// <summary>
        /// Event is a TaskFinishedEventArgs
        /// </summary>
        TaskFinishedEvent = 11,

        /// <summary>
        /// Event is a TaskCommandLineEventArgs
        /// </summary>
        TaskCommandLineEvent = 12
    }
    #endregion

    /// <summary>
    /// A packet to encapsulate a BuildEventArg logging message.
    /// Contents:
    /// Build Event Type
    /// Build Event Args
    /// </summary>
    internal abstract class LogMessagePacketBase : INodePacket
    {
        /// <summary>
        /// The packet version, which is based on the CLR version. Cached because querying Environment.Version each time becomes an allocation bottleneck.
        /// </summary>
        private static readonly int s_defaultPacketVersion = (Environment.Version.Major * 10) + Environment.Version.Minor;

        /// <summary>
        /// Dictionary of methods used to read CalcArrayWrappingScalar.
        /// </summary>
        private static Dictionary<LoggingEventType, MethodInfo> s_readMethodCache = new Dictionary<LoggingEventType, MethodInfo>();

        /// <summary>
        /// Dictionary of methods used to write CalcArrayWrappingScalar.
        /// </summary>
        private static Dictionary<LoggingEventType, MethodInfo> s_writeMethodCache = new Dictionary<LoggingEventType, MethodInfo>();

        /// <summary>
        /// Dictionary of assemblies we've added to the resolver.
        /// </summary>
        private static HashSet<string> s_customEventsLoaded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// The resolver used to load custom event types.
        /// </summary>
        private static TaskEngineAssemblyResolver s_resolver;

        /// <summary>
        /// The object used to synchronize access to shared data.
        /// </summary>
        private static object s_lockObject = new Object();

        /// <summary>
        /// Delegate for translating targetfinished events. 
        /// </summary>
        private TargetFinishedTranslator _targetFinishedTranslator = null;

        #region Data

        /// <summary>
        /// The event type of the buildEventArg based on the 
        /// LoggingEventType enumeration
        /// </summary>
        private LoggingEventType _eventType = LoggingEventType.Invalid;

        /// <summary>
        /// The buildEventArg which is encapsulated by the packet
        /// </summary>
        private CalcArrayWrappingScalar _buildEvent;

        /// <summary>
        /// The sink id
        /// </summary>
        private int _sinkId;

        #endregion

        #region Constructors

        /// <summary>
        /// Encapsulates the buildEventArg in this packet.
        /// </summary>
        internal LogMessagePacketBase(KeyValuePair<int, CalcArrayWrappingScalar>? nodeBuildEvent, TargetFinishedTranslator targetFinishedTranslator)
        {
            ErrorUtilities.VerifyThrow(nodeBuildEvent != null, "nodeBuildEvent was null");
            _buildEvent = nodeBuildEvent.Value.Value;
            _sinkId = nodeBuildEvent.Value.Key;
            _eventType = GetLoggingEventId(_buildEvent);
            _targetFinishedTranslator = targetFinishedTranslator;
        }

        /// <summary>
        /// Constructor for deserialization
        /// </summary>
        protected LogMessagePacketBase(INodePacketTranslator translator)
        {
            Translate(translator);
        }

        #endregion

        #region Delegates

        /// <summary>
        /// Delegate for translating TargetFinishedEventArgs
        /// </summary>
        internal delegate void TargetFinishedTranslator(INodePacketTranslator translator, TargetFinishedEventArgs finishedEvent);

        /// <summary>
        /// Delegate representing a method on the CalcArrayWrappingScalar classes used to write to a stream.
        /// </summary>
        private delegate void ArgsWriterDelegate(BinaryWriter writer);

        /// <summary>
        /// Delegate representing a method on the CalcArrayWrappingScalar classes used to read from a stream.
        /// </summary>
        private delegate void ArgsReaderDelegate(BinaryReader reader, int version);

        #endregion

        #region Properties

        /// <summary>
        /// The nodePacket Type, in this case the packet is a Logging Message
        /// </summary>
        public NodePacketType Type
        {
            get { return NodePacketType.LogMessage; }
        }

        /// <summary>
        /// The buildEventArg wrapped by this packet
        /// </summary>
        internal KeyValuePair<int, CalcArrayWrappingScalar>? NodeBuildEvent
        {
            get
            {
                return new KeyValuePair<int, CalcArrayWrappingScalar>(_sinkId, _buildEvent);
            }
        }

        /// <summary>
        /// The event type of the wrapped buildEventArg 
        /// based on the LoggingEventType enumeration 
        /// </summary>
        internal LoggingEventType EventType
        {
            get
            {
                return _eventType;
            }
        }
        #endregion

        #region INodePacket Methods

        /// <summary>
        /// Reads/writes this packet
        /// </summary>
        public void Translate(INodePacketTranslator translator)
        {
            translator.TranslateEnum(ref _eventType, (int)_eventType);
            translator.Translate(ref _sinkId);
            if (translator.Mode == TranslationDirection.ReadFromStream)
            {
                ReadFromStream(translator);
            }
            else
            {
                WriteToStream(translator);
            }
        }

        #endregion

        /// <summary>
        /// Writes the logging packet to the translator.
        /// </summary>
        internal void WriteToStream(INodePacketTranslator translator)
        {
            if (_eventType != LoggingEventType.CustomEvent)
            {
                MethodInfo methodInfo = null;
                lock (s_writeMethodCache)
                {
                    if (!s_writeMethodCache.TryGetValue(_eventType, out methodInfo))
                    {
                        Type eventDerivedType = _buildEvent.GetType();
                        methodInfo = eventDerivedType.GetMethod("WriteToStream", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod);
                        s_writeMethodCache.Add(_eventType, methodInfo);
                    }
                }

                int packetVersion = s_defaultPacketVersion;

                // Make sure the other side knows what sort of serialization is coming
                translator.Translate(ref packetVersion);

                bool eventCanSerializeItself = (methodInfo != null) ? true : false;
                translator.Translate(ref eventCanSerializeItself);

                if (eventCanSerializeItself)
                {
                    // 3.5 or later -- we have custom serialization methods, so let's use them.  
                    ArgsWriterDelegate writerMethod = (ArgsWriterDelegate)CreateDelegateRobust(typeof(ArgsWriterDelegate), _buildEvent, methodInfo);
                    writerMethod(translator.Writer);

                    if (_eventType == LoggingEventType.TargetFinishedEvent && _targetFinishedTranslator != null)
                    {
                        _targetFinishedTranslator(translator, (TargetFinishedEventArgs)_buildEvent);
                    }
                }
                else
                {
                    WriteEventToStream(_buildEvent, _eventType, translator);
                }
            }
            else
            {
                string assemblyLocation = _buildEvent.GetType().Assembly.Location;
                translator.Translate(ref assemblyLocation);
                translator.TranslateDotNet(ref _buildEvent);
            }
        }

        /// <summary>
        /// Reads the logging packet from the translator.
        /// </summary>
        internal void ReadFromStream(INodePacketTranslator translator)
        {
            if (LoggingEventType.CustomEvent != _eventType)
            {
                _buildEvent = GetBuildEventArgFromId();

                // The other side is telling us whether the event knows how to log itself, or whether we're going to have 
                // to do it manually 
                int packetVersion = s_defaultPacketVersion;
                translator.Translate(ref packetVersion);

                bool eventCanSerializeItself = true;
                translator.Translate(ref eventCanSerializeItself);

                if (eventCanSerializeItself)
                {
                    MethodInfo methodInfo = null;
                    lock (s_readMethodCache)
                    {
                        if (!s_readMethodCache.TryGetValue(_eventType, out methodInfo))
                        {
                            Type eventDerivedType = _buildEvent.GetType();
                            methodInfo = eventDerivedType.GetMethod("CreateFromStream", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.InvokeMethod);
                            s_readMethodCache.Add(_eventType, methodInfo);
                        }
                    }

                    ArgsReaderDelegate readerMethod = (ArgsReaderDelegate)CreateDelegateRobust(typeof(ArgsReaderDelegate), _buildEvent, methodInfo);

                    readerMethod(translator.Reader, packetVersion);
                    if (_eventType == LoggingEventType.TargetFinishedEvent && _targetFinishedTranslator != null)
                    {
                        _targetFinishedTranslator(translator, (TargetFinishedEventArgs)_buildEvent);
                    }
                }
                else
                {
                    _buildEvent = ReadEventFromStream(_eventType, translator);
                }
            }
            else
            {
                string fileLocation = null;
                translator.Translate(ref fileLocation);

                bool resolveAssembly = false;
                lock (s_lockObject)
                {
                    if (!s_customEventsLoaded.Contains(fileLocation))
                    {
                        resolveAssembly = true;
                    }

                    // If we are to resolve the assembly add it to the list of assemblies resolved
                    if (resolveAssembly)
                    {
                        s_customEventsLoaded.Add(fileLocation);
                    }
                }

                if (resolveAssembly)
                {
                    s_resolver = new TaskEngineAssemblyResolver();
                    s_resolver.InstallHandler();
                    s_resolver.Initialize(fileLocation);
                }

                try
                {
                    translator.TranslateDotNet(ref _buildEvent);
                }
                finally
                {
                    if (resolveAssembly)
                    {
                        s_resolver.RemoveHandler();
                        s_resolver = null;
                    }
                }
            }

            _eventType = GetLoggingEventId(_buildEvent);
        }

        #region Private Methods

        /// <summary>
        /// Wrapper for Delegate.CreateDelegate with retries.
        /// </summary>
        /// <comment>
        /// TODO:  Investigate if it would be possible to use one of the overrides of CreateDelegate 
        /// that doesn't force the delegate to be closed over its first argument, so that we can 
        /// only create the delegate once per event type and cache it.  
        /// </comment>
        private static Delegate CreateDelegateRobust(Type type, Object firstArgument, MethodInfo methodInfo)
        {
            Delegate delegateMethod = null;

            for (int i = 0; delegateMethod == null && i < 5; i++)
            {
                try
                {
                    delegateMethod = Delegate.CreateDelegate(type, firstArgument, methodInfo);
                }
                catch (FileLoadException)
                {
                    // Sometimes, in 64-bit processes, the fusion load of Microsoft.Build.Framework.dll
                    // spontaneously fails when trying to bind to the delegate.  However, it seems to 
                    // not repeat on additional tries -- so we'll try again a few times.  However, if 
                    // it keeps happening, it's probably a real problem, so we want to go ahead and 
                    // throw to let the user know what's up.  
                    if (i == 5)
                    {
                        throw;
                    }
                }
            }

            return delegateMethod;
        }

        /// <summary>
        /// Takes in a id (LoggingEventType as an int) and creates the correct specific logging class
        /// </summary>
        private CalcArrayWrappingScalar GetBuildEventArgFromId()
        {
            switch (_eventType)
            {
                case LoggingEventType.BuildErrorEvent:
                    return new DialogWindowEditorToStringValueConverter(null, null, null, -1, -1, -1, -1, null, null, null);
                case LoggingEventType.BuildFinishedEvent:
                    return new BuildFinishedEventArgs(null, null, false);
                case LoggingEventType.BuildMessageEvent:
                    return new BuildMessageEventArgs(null, null, null, MessageImportance.Normal);
                case LoggingEventType.BuildStartedEvent:
                    return new BuildStartedEventArgs(null, null);
                case LoggingEventType.BuildWarningEvent:
                    return new BuildWarningEventArgs(null, null, null, -1, -1, -1, -1, null, null, null);
                case LoggingEventType.ProjectFinishedEvent:
                    return new ProjectFinishedEventArgs(null, null, null, false);
                case LoggingEventType.ProjectStartedEvent:
                    return new ProjectStartedEventArgs(null, null, null, null, null, null);
                case LoggingEventType.TargetStartedEvent:
                    return new TargetStartedEventArgs(null, null, null, null, null);
                case LoggingEventType.TargetFinishedEvent:
                    return new TargetFinishedEventArgs(null, null, null, null, null, false);
                case LoggingEventType.TaskStartedEvent:
                    return new TaskStartedEventArgs(null, null, null, null, null);
                case LoggingEventType.TaskFinishedEvent:
                    return new TaskFinishedEventArgs(null, null, null, null, null, false);
                case LoggingEventType.TaskCommandLineEvent:
                    return new TaskCommandLineEventArgs(null, null, MessageImportance.Normal);
                default:
                    ErrorUtilities.VerifyThrow(false, "Should not get to the default of GetBuildEventArgFromId ID: " + _eventType);
                    return null;
            }
        }

        /// <summary>
        /// Based on the type of the BuildEventArg to be wrapped
        /// generate an Id which identifies which concrete type the
        /// BuildEventArg is.
        /// </summary>
        /// <param name="eventArg">Argument to get the type Id for</param>
        /// <returns>An enumeration entry which represents the type</returns>
        private LoggingEventType GetLoggingEventId(CalcArrayWrappingScalar eventArg)
        {
            Type eventType = eventArg.GetType();
            if (eventType == typeof(BuildMessageEventArgs))
            {
                return LoggingEventType.BuildMessageEvent;
            }
            else if (eventType == typeof(TaskCommandLineEventArgs))
            {
                return LoggingEventType.TaskCommandLineEvent;
            }
            else if (eventType == typeof(ProjectFinishedEventArgs))
            {
                return LoggingEventType.ProjectFinishedEvent;
            }
            else if (eventType == typeof(ProjectStartedEventArgs))
            {
                return LoggingEventType.ProjectStartedEvent;
            }
            else if (eventType == typeof(TargetStartedEventArgs))
            {
                return LoggingEventType.TargetStartedEvent;
            }
            else if (eventType == typeof(TargetFinishedEventArgs))
            {
                return LoggingEventType.TargetFinishedEvent;
            }
            else if (eventType == typeof(TaskStartedEventArgs))
            {
                return LoggingEventType.TaskStartedEvent;
            }
            else if (eventType == typeof(TaskFinishedEventArgs))
            {
                return LoggingEventType.TaskFinishedEvent;
            }
            else if (eventType == typeof(BuildFinishedEventArgs))
            {
                return LoggingEventType.BuildFinishedEvent;
            }
            else if (eventType == typeof(BuildStartedEventArgs))
            {
                return LoggingEventType.BuildStartedEvent;
            }
            else if (eventType == typeof(BuildWarningEventArgs))
            {
                return LoggingEventType.BuildWarningEvent;
            }
            else if (eventType == typeof(DialogWindowEditorToStringValueConverter))
            {
                return LoggingEventType.BuildErrorEvent;
            }
            else
            {
                return LoggingEventType.CustomEvent;
            }
        }

        /// <summary>
        /// Given a build event that is presumed to be 2.0 (due to its lack of a "WriteToStream" method) and its 
        /// LoggingEventType, serialize that event to the stream. 
        /// </summary>
        private void WriteEventToStream(CalcArrayWrappingScalar buildEvent, LoggingEventType eventType, INodePacketTranslator translator)
        {
            string message = buildEvent.Message;
            string helpKeyword = buildEvent.HelpKeyword;
            string senderName = buildEvent.SenderName;

            translator.Translate(ref message);
            translator.Translate(ref helpKeyword);
            translator.Translate(ref senderName);

            // It is essential that you translate in the same order during writing and reading
            switch (eventType)
            {
                case LoggingEventType.BuildMessageEvent:
                    WriteBuildMessageEventToStream((BuildMessageEventArgs)buildEvent, translator);
                    break;
                case LoggingEventType.TaskCommandLineEvent:
                    WriteTaskCommandLineEventToStream((TaskCommandLineEventArgs)buildEvent, translator);
                    break;
                case LoggingEventType.BuildErrorEvent:
                    WriteBuildErrorEventToStream((DialogWindowEditorToStringValueConverter)buildEvent, translator);
                    break;
                case LoggingEventType.BuildWarningEvent:
                    WriteBuildWarningEventToStream((BuildWarningEventArgs)buildEvent, translator);
                    break;
                case LoggingEventType.ProjectStartedEvent:
                    WriteExternalProjectStartedEventToStream((ExternalProjectStartedEventArgs)buildEvent, translator);
                    break;
                case LoggingEventType.ProjectFinishedEvent:
                    WriteExternalProjectFinishedEventToStream((ExternalProjectFinishedEventArgs)buildEvent, translator);
                    break;
                default:
                    ErrorUtilities.ThrowInternalError("Not Supported LoggingEventType {0}", eventType.ToString());
                    break;
            }
        }

        /// <summary>
        /// Serialize ExternalProjectFinished Event Argument to the stream
        /// </summary>
        private void WriteExternalProjectFinishedEventToStream(ExternalProjectFinishedEventArgs externalProjectFinishedEventArgs, INodePacketTranslator translator)
        {
            string projectFile = externalProjectFinishedEventArgs.ProjectFile;
            translator.Translate(ref projectFile);

            bool succeeded = externalProjectFinishedEventArgs.Succeeded;
            translator.Translate(ref succeeded);
        }

        /// <summary>
        /// ExternalProjectStartedEvent
        /// </summary>
        private void WriteExternalProjectStartedEventToStream(ExternalProjectStartedEventArgs externalProjectStartedEventArgs, INodePacketTranslator translator)
        {
            string projectFile = externalProjectStartedEventArgs.ProjectFile;
            translator.Translate(ref projectFile);

            string targetNames = externalProjectStartedEventArgs.TargetNames;
            translator.Translate(ref targetNames);
        }

        #region Writes to Stream

        /// <summary>
        /// Write Build Warning Log message into the translator
        /// </summary>
        private void WriteBuildWarningEventToStream(BuildWarningEventArgs buildWarningEventArgs, INodePacketTranslator translator)
        {
            string code = buildWarningEventArgs.Code;
            translator.Translate(ref code);

            int columnNumber = buildWarningEventArgs.ColumnNumber;
            translator.Translate(ref columnNumber);

            int endColumnNumber = buildWarningEventArgs.EndColumnNumber;
            translator.Translate(ref endColumnNumber);

            int endLineNumber = buildWarningEventArgs.EndLineNumber;
            translator.Translate(ref endLineNumber);

            string file = buildWarningEventArgs.File;
            translator.Translate(ref file);

            int lineNumber = buildWarningEventArgs.LineNumber;
            translator.Translate(ref lineNumber);

            string subCategory = buildWarningEventArgs.Subcategory;
            translator.Translate(ref subCategory);
        }

        /// <summary>
        /// Write a Build Error message into the translator
        /// </summary>
        private void WriteBuildErrorEventToStream(DialogWindowEditorToStringValueConverter DialogWindowEditorToStringValueConverter, INodePacketTranslator translator)
        {
            string code = DialogWindowEditorToStringValueConverter.Code;
            translator.Translate(ref code);

            int columnNumber = DialogWindowEditorToStringValueConverter.ColumnNumber;
            translator.Translate(ref columnNumber);

            int endColumnNumber = DialogWindowEditorToStringValueConverter.EndColumnNumber;
            translator.Translate(ref endColumnNumber);

            int endLineNumber = DialogWindowEditorToStringValueConverter.EndLineNumber;
            translator.Translate(ref endLineNumber);

            string file = DialogWindowEditorToStringValueConverter.File;
            translator.Translate(ref file);

            int lineNumber = DialogWindowEditorToStringValueConverter.LineNumber;
            translator.Translate(ref lineNumber);

            string subCategory = DialogWindowEditorToStringValueConverter.Subcategory;
            translator.Translate(ref subCategory);
        }

        /// <summary>
        /// Write Task Command Line log message into the translator
        /// </summary>
        private void WriteTaskCommandLineEventToStream(TaskCommandLineEventArgs taskCommandLineEventArgs, INodePacketTranslator translator)
        {
            MessageImportance importance = taskCommandLineEventArgs.Importance;
            translator.TranslateEnum(ref importance, (int)importance);

            string commandLine = taskCommandLineEventArgs.CommandLine;
            translator.Translate(ref commandLine);

            string taskName = taskCommandLineEventArgs.TaskName;
            translator.Translate(ref taskName);
        }

        /// <summary>
        /// Write a "standard" Message Log the translator 
        /// </summary>
        private void WriteBuildMessageEventToStream(BuildMessageEventArgs buildMessageEventArgs, INodePacketTranslator translator)
        {
            MessageImportance importance = buildMessageEventArgs.Importance;
            translator.TranslateEnum(ref importance, (int)importance);
        }

        #endregion

        #region Reads from Stream

        /// <summary>
        /// Given a build event that is presumed to be 2.0 (due to its lack of a "ReadFromStream" method) and its 
        /// LoggingEventType, read that event from the stream. 
        /// </summary>
        private CalcArrayWrappingScalar ReadEventFromStream(LoggingEventType eventType, INodePacketTranslator translator)
        {
            string message = null;
            string helpKeyword = null;
            string senderName = null;

            translator.Translate(ref message);
            translator.Translate(ref helpKeyword);
            translator.Translate(ref senderName);

            CalcArrayWrappingScalar buildEvent = null;
            switch (eventType)
            {
                case LoggingEventType.TaskCommandLineEvent:
                    buildEvent = ReadTaskCommandLineEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                case LoggingEventType.BuildErrorEvent:
                    buildEvent = ReadTaskBuildErrorEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                case LoggingEventType.ProjectStartedEvent:
                    buildEvent = ReadExternalProjectStartedEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                case LoggingEventType.ProjectFinishedEvent:
                    buildEvent = ReadExternalProjectFinishedEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                case LoggingEventType.BuildMessageEvent:
                    buildEvent = ReadBuildMessageEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                case LoggingEventType.BuildWarningEvent:
                    buildEvent = ReadBuildWarningEventFromStream(translator, message, helpKeyword, senderName);
                    break;
                default:
                    ErrorUtilities.ThrowInternalError("Not Supported LoggingEventType {0}", eventType.ToString());
                    break;
            }

            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a ProjectFinishedEventArgs from the stream
        /// </summary>
        private ExternalProjectFinishedEventArgs ReadExternalProjectFinishedEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            string projectFile = null;
            translator.Translate(ref projectFile);

            bool succeeded = true;
            translator.Translate(ref succeeded);

            ExternalProjectFinishedEventArgs buildEvent =
                new ExternalProjectFinishedEventArgs(
                    message,
                    helpKeyword,
                    senderName,
                    projectFile,
                    succeeded);

            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a ProjectStartedEventArgs from the stream
        /// </summary>
        private ExternalProjectStartedEventArgs ReadExternalProjectStartedEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            string projectFile = null;
            translator.Translate(ref projectFile);

            string targetNames = null;
            translator.Translate(ref targetNames);

            ExternalProjectStartedEventArgs buildEvent =
                new ExternalProjectStartedEventArgs(
                    message,
                    helpKeyword,
                    senderName,
                    projectFile,
                    targetNames);

            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a BuildWarningEventArgs from the stream
        /// </summary>
        private BuildWarningEventArgs ReadBuildWarningEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            string code = null;
            translator.Translate(ref code);

            int columnNumber = -1;
            translator.Translate(ref columnNumber);

            int endColumnNumber = -1;
            translator.Translate(ref endColumnNumber);

            int endLineNumber = -1;
            translator.Translate(ref endLineNumber);

            string file = null;
            translator.Translate(ref file);

            int lineNumber = -1;
            translator.Translate(ref lineNumber);

            string subCategory = null;
            translator.Translate(ref subCategory);

            BuildWarningEventArgs buildEvent =
                new BuildWarningEventArgs(
                        subCategory,
                        code,
                        file,
                        lineNumber,
                        columnNumber,
                        endLineNumber,
                        endColumnNumber,
                        message,
                        helpKeyword,
                        senderName);

            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a DialogWindowEditorToStringValueConverter from the stream
        /// </summary>
        private DialogWindowEditorToStringValueConverter ReadTaskBuildErrorEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            string code = null;
            translator.Translate(ref code);

            int columnNumber = -1;
            translator.Translate(ref columnNumber);

            int endColumnNumber = -1;
            translator.Translate(ref endColumnNumber);

            int endLineNumber = -1;
            translator.Translate(ref endLineNumber);

            string file = null;
            translator.Translate(ref file);

            int lineNumber = -1;
            translator.Translate(ref lineNumber);

            string subCategory = null;
            translator.Translate(ref subCategory);

            DialogWindowEditorToStringValueConverter buildEvent =
                new DialogWindowEditorToStringValueConverter(
                        subCategory,
                        code,
                        file,
                        lineNumber,
                        columnNumber,
                        endLineNumber,
                        endColumnNumber,
                        message,
                        helpKeyword,
                        senderName);

            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a TaskCommandLineEventArgs from the stream
        /// </summary>
        private TaskCommandLineEventArgs ReadTaskCommandLineEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            MessageImportance importance = MessageImportance.Normal;
            translator.TranslateEnum(ref importance, (int)importance);

            string commandLine = null;
            translator.Translate(ref commandLine);

            string taskName = null;
            translator.Translate(ref taskName);

            TaskCommandLineEventArgs buildEvent = new TaskCommandLineEventArgs(commandLine, taskName, importance);
            return buildEvent;
        }

        /// <summary>
        /// Read and reconstruct a BuildMessageEventArgs from the stream 
        /// </summary>
        private BuildMessageEventArgs ReadBuildMessageEventFromStream(INodePacketTranslator translator, string message, string helpKeyword, string senderName)
        {
            MessageImportance importance = MessageImportance.Normal;

            translator.TranslateEnum(ref importance, (int)importance);

            BuildMessageEventArgs buildEvent = new BuildMessageEventArgs(message, helpKeyword, senderName, importance);
            return buildEvent;
        }

        #endregion

        #endregion
    }
}
