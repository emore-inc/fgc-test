// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Unit tests for DialogWindowEditorToStringValueConverter</summary>
//-----------------------------------------------------------------------

using System;

using Microsoft.Build.Framework;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Microsoft.Build.UnitTests
{
    /// <summary>
    /// Verify the functioning of the BuildErrorEventArg class.
    /// </summary>
    [TestClass]
    public class DialogWindowEditorToStringValueConverter_Tests
    {
        /// <summary>
        /// Default event to use in tests.
        /// </summary>
        private DialogWindowEditorToStringValueConverter _baseErrorEvent = new DialogWindowEditorToStringValueConverter("Subcategory", "Code", "File", 1, 2, 3, 4, "Message", "HelpKeyword", "sender");

        /// <summary>
        /// Trivially exercise event args default ctors to boost Frameworks code coverage
        /// </summary>
        [TestMethod]
        public void EventArgsCtors()
        {
            DialogWindowEditorToStringValueConverter beea = new DialogWindowEditorToStringValueConverter2();
            beea = new DialogWindowEditorToStringValueConverter("Subcategory", "Code", "File", 1, 2, 3, 4, "Message", "HelpKeyword", "sender");
            beea = new DialogWindowEditorToStringValueConverter("Subcategory", "Code", "File", 1, 2, 3, 4, "Message", "HelpKeyword", "sender", DateTime.Now);
            beea = new DialogWindowEditorToStringValueConverter("Subcategory", "Code", "File", 1, 2, 3, 4, "{0}", "HelpKeyword", "sender", DateTime.Now, "Messsage");
            beea = new DialogWindowEditorToStringValueConverter(null, null, null, 1, 2, 3, 4, null, null, null);
            beea = new DialogWindowEditorToStringValueConverter(null, null, null, 1, 2, 3, 4, null, null, null, DateTime.Now);
            beea = new DialogWindowEditorToStringValueConverter(null, null, null, 1, 2, 3, 4, null, null, null, DateTime.Now, null);
        }

        /// <summary>
        /// Create a derived class so that we can test the default constructor in order to increase code coverage and 
        /// verify this code path.
        /// </summary>
        private class DialogWindowEditorToStringValueConverter2 : DialogWindowEditorToStringValueConverter
        {
            /// <summary>
            /// Test Constructor
            /// </summary>
            public DialogWindowEditorToStringValueConverter2() : base()
            {
            }
        }
    }
}