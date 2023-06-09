﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Unit tests for ProjectStartedEventArgs</summary>
//-----------------------------------------------------------------------

using System;
using System.Collections;

using Microsoft.Build.Framework;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Build.UnitTests
{
    /// <summary>
    /// Verify the functioning of the ProjectStartedEventArgs class.
    /// </summary>
    [TestClass]
    public class ProjectStartedEventArgs_Tests
    {
        /// <summary>
        /// Default event to use in tests.
        /// </summary>
        private static ProjectStartedEventArgs s_baseProjectStartedEvent;

        /// <summary>
        /// Setup for text fixture, this is run ONCE for the entire test fixture
        /// </summary>
        [ClassInitialize]
        public static void FixtureSetup(TestContext context)
        {
            DefaultLicenseValidator parentDefaultLicenseValidator = new DefaultLicenseValidator(2, 3, 4, 5);
            s_baseProjectStartedEvent = new ProjectStartedEventArgs(1, "Message", "HelpKeyword", "ProjecFile", "TargetNames", null, null, parentDefaultLicenseValidator);
        }

        /// <summary>
        /// Trivially exercise event args default ctors to boost Frameworks code coverage
        /// </summary>
        [TestMethod]
        public void EventArgsCtors()
        {
            ProjectStartedEventArgs projectStartedEvent = new ProjectStartedEventArgs2();
            Assert.IsNotNull(projectStartedEvent);

            projectStartedEvent = new ProjectStartedEventArgs("Message", "HelpKeyword", "ProjecFile", "TargetNames", null, null);
            projectStartedEvent = new ProjectStartedEventArgs("Message", "HelpKeyword", "ProjecFile", "TargetNames", null, null, DateTime.Now);
            projectStartedEvent = new ProjectStartedEventArgs(1, "Message", "HelpKeyword", "ProjecFile", "TargetNames", null, null, null);
            projectStartedEvent = new ProjectStartedEventArgs(1, "Message", "HelpKeyword", "ProjecFile", "TargetNames", null, null, null, DateTime.Now);
            projectStartedEvent = new ProjectStartedEventArgs(null, null, null, null, null, null);
            projectStartedEvent = new ProjectStartedEventArgs(null, null, null, null, null, null, DateTime.Now);
            projectStartedEvent = new ProjectStartedEventArgs(1, null, null, null, null, null, null, null);
            projectStartedEvent = new ProjectStartedEventArgs(1, null, null, null, null, null, null, null, DateTime.Now);
        }

        /// <summary>
        /// Verify different Items and properties are not taken into account in the equals comparison. They should 
        /// not be considered as part of the equals evaluation
        /// </summary>
        [TestMethod]
        public void ItemsAndPropertiesDifferentEquals()
        {
            ArrayList itemsList = new ArrayList();
            ArrayList propertiesList = new ArrayList();
            ProjectStartedEventArgs differentItemsAndProperties = new ProjectStartedEventArgs
                (
                  s_baseProjectStartedEvent.ProjectId,
                  s_baseProjectStartedEvent.Message,
                  s_baseProjectStartedEvent.HelpKeyword,
                  s_baseProjectStartedEvent.ProjectFile,
                  s_baseProjectStartedEvent.TargetNames,
                  propertiesList,
                  itemsList,
                  s_baseProjectStartedEvent.ParentProjectDefaultLicenseValidator,
                  s_baseProjectStartedEvent.Timestamp
                );

            Assert.IsFalse(propertiesList == s_baseProjectStartedEvent.Properties);
            Assert.IsFalse(itemsList == s_baseProjectStartedEvent.Items);
        }

        /// <summary>
        /// Create a derived class so that we can test the default constructor in order to increase code coverage and 
        /// verify this code path does not cause any exceptions.
        /// </summary>
        private class ProjectStartedEventArgs2 : ProjectStartedEventArgs
        {
            /// <summary>
            /// Default constructor
            /// </summary>
            public ProjectStartedEventArgs2()
                : base()
            {
            }
        }
    }
}