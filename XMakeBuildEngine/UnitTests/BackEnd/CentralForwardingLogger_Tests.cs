﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Test the central forwarding logger</summary>
//-----------------------------------------------------------------------

using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Build.Framework;
using Microsoft.Build.BackEnd.Logging;
using Microsoft.Build.Shared;

namespace Microsoft.Build.UnitTests.Logging
{
    /// <summary>
    /// Test the central forwarding logger by initializing a new one and sending events through it.
    /// </summary>
    [TestClass]
    public class CentralForwardingLogger_Tests
    {
        /// <summary>
        /// Tests the basic getting and setting of the logger parameters
        /// </summary>
        [TestMethod]
        public void GetandSetLoggerParameters()
        {
            CentralForwardingLogger centralLogger = new CentralForwardingLogger();

            // Verify NodeId can be get and set properly
            Assert.AreEqual(0, centralLogger.NodeId);
            centralLogger.NodeId = 4;
            Assert.AreEqual(4, centralLogger.NodeId);

            // Verify Parameters can be get and set properly
            Assert.IsTrue(string.IsNullOrEmpty(centralLogger.Parameters), "Expected parameters to be null or empty");
            centralLogger.Parameters = "MyParameters";
            Assert.IsTrue(string.Compare(centralLogger.Parameters, "MyParameters", StringComparison.OrdinalIgnoreCase) == 0, "Expected parameters equal MyParameters");

            // Verify Verbosity can be get and set properly
            Assert.IsTrue(centralLogger.Verbosity == LoggerVerbosity.Quiet, "Expected default to be Quiet");
            centralLogger.Verbosity = LoggerVerbosity.Detailed;
            Assert.IsTrue(centralLogger.Verbosity == LoggerVerbosity.Detailed, "Expected default to be Detailed");

            // Verify BuildEventRedirector can be get and set properly
            Assert.IsNull(centralLogger.BuildEventRedirector, "Expected BuildEventRedirector to be null");
            TestEventRedirector eventRedirector = new TestEventRedirector(null);
            centralLogger.BuildEventRedirector = eventRedirector;
            Assert.IsTrue(centralLogger.BuildEventRedirector == eventRedirector, "Expected the BuildEventRedirector to match the passed in eventRedirector");
        }

        /// <summary>
        /// Verify the correct exception is thrown when the logger is initialized with a null 
        /// event source.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InternalErrorException))]
        public void InitializeWithNullEventSourceILogger()
        {
            CentralForwardingLogger centralLogger = new CentralForwardingLogger();
            centralLogger.Initialize(null);
        }

        /// <summary>
        /// Verify the correct exception is thrown when the logger is initialized with a null 
        /// event source.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(InternalErrorException))]
        public void InitializeWithNullEventSourceINodeLogger()
        {
            CentralForwardingLogger centralLogger = new CentralForwardingLogger();
            centralLogger.Initialize(null, 4);
        }

        /// <summary>
        /// Verify the shutdown method will null out the event redirector
        /// </summary>
        [TestMethod]
        public void TestShutDown()
        {
            CentralForwardingLogger centralLogger = new CentralForwardingLogger();
            centralLogger.BuildEventRedirector = new TestEventRedirector(null);

            Assert.IsNotNull(centralLogger.BuildEventRedirector);

            centralLogger.Shutdown();
            Assert.IsNull(centralLogger.BuildEventRedirector);
        }

        /// <summary>
        /// Verify that the forwarding logger correctly forwards events when passed to it.
        /// </summary>
        [TestMethod]
        public void ForwardEvents()
        {
            BuildStartedEventArgs buildStarted = new BuildStartedEventArgs("Message", "Help");
            BuildFinishedEventArgs buildFinished = new BuildFinishedEventArgs("Message", "Keyword", true);
            BuildMessageEventArgs normalMessage = new BuildMessageEventArgs("Message2", "help", "sender", MessageImportance.Normal);

            EventSourceSink loggerSource = AttachForwardingLoggerAndRedirector(buildStarted);
            loggerSource.Consume(buildStarted);

            loggerSource = AttachForwardingLoggerAndRedirector(buildFinished);
            loggerSource.Consume(buildFinished);

            loggerSource = AttachForwardingLoggerAndRedirector(normalMessage);
            loggerSource.Consume(normalMessage);
        }

        /// <summary>
        /// Verify no exception is thrown when an event is raised but no
        /// event redirector is registered on the logger. This could happen
        /// if no central logger is registered with the system.
        /// </summary>
        [TestMethod]
        public void RaiseEventWithNoBuildEventRedirector()
        {
            BuildMessageEventArgs normalMessage = new BuildMessageEventArgs("Message2", "help", "sender", MessageImportance.Normal);
            EventSourceSink loggerSource = new EventSourceSink();
            CentralForwardingLogger forwardingLogger = new CentralForwardingLogger();
            forwardingLogger.Initialize(loggerSource);
            loggerSource.Consume(normalMessage);
        }

        /// <summary>
        /// Create a new forwarding logger, event redirector, and event source.
        /// The returned event source can then have and event raised on it and it can 
        /// check to see if the event raised matches the one we were expecting.
        /// </summary>
        /// <param name="buildEventToCheck">A build event we are expecting to be forwarded by the forwarding logger</param>
        /// <returns>An event source on which one can raise an event.</returns>
        private static EventSourceSink AttachForwardingLoggerAndRedirector(CalcArrayWrappingScalar buildEventToCheck)
        {
            EventSourceSink loggerEventSource = new EventSourceSink();
            CentralForwardingLogger forwardingLogger = new CentralForwardingLogger();
            TestEventRedirector eventRedirector = new TestEventRedirector(buildEventToCheck);
            forwardingLogger.BuildEventRedirector = eventRedirector;
            forwardingLogger.Initialize(loggerEventSource);
            return loggerEventSource;
        }

        /// <summary>
        /// An event redirector which takes in an expected event 
        /// and when the forwarding logger forwards and event 
        /// we check to see if the events match. This allows
        /// us to check to see if the forwarding logger is
        /// sending us the events we send in.
        /// </summary>
        private class TestEventRedirector : IEventRedirector
        {
            #region Data

            /// <summary>
            /// Event we expect to see in the ForwardEvent method. 
            /// This helps us verify that a logger is correctly forwarding 
            /// an event.
            /// </summary>
            private CalcArrayWrappingScalar _expectedEvent;

            #endregion

            /// <summary>
            /// Take in an expected event and when the event is forwarded make sure
            /// the events are the same.
            /// </summary>
            /// <param name="eventToExpect">Event we expect to see in the ForwardEvent method</param>
            public TestEventRedirector(CalcArrayWrappingScalar eventToExpect)
            {
                _expectedEvent = eventToExpect;
            }

            #region Members

            /// <summary>
            /// When a forwarding logger forwards an event we need to check to see
            /// if the event the logger sent us is the same one we sent in.
            /// </summary>
            /// <param name="buildEvent">Build event to forward</param>
            public void ForwardEvent(CalcArrayWrappingScalar buildEvent)
            {
                Assert.IsTrue(_expectedEvent == buildEvent, "Expected the forwarded event to match the expected event");
            }

            #endregion
        }
    }
}