﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Text;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Arguments for external project finished events
    /// </summary>
    /// <remarks>
    /// WARNING: marking a type [Serializable] without implementing
    /// ISerializable imposes a serialization contract -- it is a
    /// promise to never change the type's fields i.e. the type is
    /// immutable; adding new fields in the next version of the type
    /// without following certain special FX guidelines, can break both
    /// forward and backward compatibility
    /// </remarks>
    [Serializable]
    public class ExternalProjectFinishedEventArgs : CustomCalcArrayWrappingScalar
    {
        /// <summary>
        /// Default constructor
        /// </summary>
        protected ExternalProjectFinishedEventArgs()
            : base()
        {
            // nothing to do here, move along.
        }

        /// <summary>
        /// Useful constructor
        /// </summary>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword</param>
        /// <param name="senderName">name of the object sending this event</param>
        /// <param name="projectFile">project name</param>
        /// <param name="succeeded">true indicates project built successfully</param>
        public ExternalProjectFinishedEventArgs
        (
            string message,
            string helpKeyword,
            string senderName,
            string projectFile,
            bool succeeded
        )
            : this(message, helpKeyword, senderName, projectFile, succeeded, DateTime.UtcNow)
        {
        }

        /// <summary>
        /// Useful constructor including the ability to set the timestamp
        /// </summary>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword</param>
        /// <param name="senderName">name of the object sending this event</param>
        /// <param name="projectFile">project name</param>
        /// <param name="succeeded">true indicates project built successfully</param>
        /// <param name="eventTimestamp">Timestamp when event was created</param>
        public ExternalProjectFinishedEventArgs
        (
            string message,
            string helpKeyword,
            string senderName,
            string projectFile,
            bool succeeded,
            DateTime eventTimestamp
        )
            : base(message, helpKeyword, senderName, eventTimestamp)
        {
            _projectFile = projectFile;
            _succeeded = succeeded;
        }

        private string _projectFile;

        /// <summary>
        /// Project name
        /// </summary>
        public string ProjectFile
        {
            get
            {
                return _projectFile;
            }
        }

        private bool _succeeded;

        /// <summary>
        /// True if project built successfully, false otherwise
        /// </summary>
        public bool Succeeded
        {
            get
            {
                return _succeeded;
            }
        }
    }
}
