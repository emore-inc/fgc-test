﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Reflection;
using System.Globalization;
using System.Runtime.Serialization;
using System.Security.Permissions;

using Microsoft.Build.Shared;

namespace Microsoft.Build.CommandLine
{
    /// <summary>
    /// This exception is used to flag failures in application initialization, either due to invalid parameters on the command
    /// line, or because the application was invoked in an invalid context.
    /// </summary>
    /// <remarks>
    /// Unlike the CommandLineSwitchException, this exception is NOT thrown for syntax errors in switches.
    /// </remarks>
    [Serializable]
    internal sealed class InitializationException : Exception
    {
        /// <summary>
        /// Private default constructor prevents parameterless instantiation.
        /// </summary>
        private InitializationException()
        {
            // do nothing
        }

        /// <summary>
        /// This constructor initializes the exception message.
        /// </summary>
        /// <param name="message"></param>
        private InitializationException
        (
            string message
        ) :
            base(message)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor initializes the exception message and saves the switch that caused the initialization failure.
        /// </summary>
        /// <param name="message"></param>
        /// <param name="invalidSwitch">Can be null.</param>
        private InitializationException
        (
            string message,
            string invalidSwitch
        ) :
            this(message)
        {
            _invalidSwitch = invalidSwitch;
        }

        /// <summary>
        /// Serialization constructor
        /// </summary>
        private InitializationException
        (
            SerializationInfo info,
            StreamingContext context
        ) :
            base(info, context)

        {
            ErrorUtilities.VerifyThrowArgumentNull(info, "info");

            _invalidSwitch = info.GetString("invalidSwitch");
        }

        /// <summary>
        /// Gets the error message and the invalid switch, or only the error message if no invalid switch is set.
        /// </summary>
        public override string Message
        {
            get
            {
                if (_invalidSwitch == null)
                {
                    return base.Message;
                }
                else
                {
                    return base.Message + Environment.NewLine + ResourceUtilities.FormatResourceString("InvalidSwitchIndicator", _invalidSwitch);
                }
            }
        }

        // the invalid switch causing this exception (can be null)
        private string _invalidSwitch;

        /// <summary>
        /// Serialize the contents of the class.
        /// </summary>
        [SecurityPermissionAttribute(SecurityAction.Demand, SerializationFormatter = true)]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);

            info.AddValue("invalidSwitch", _invalidSwitch, typeof(string));
        }

        /// <summary>
        /// Throws the exception if the specified condition is not met.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="messageResourceName"></param>
        internal static void VerifyThrow(bool condition, string messageResourceName)
        {
            VerifyThrow(condition, messageResourceName, null);
        }

        /// <summary>
        /// Throws the exception if the specified condition is not met.
        /// </summary>
        /// <param name="condition"></param>
        /// <param name="messageResourceName"></param>
        /// <param name="invalidSwitch"></param>
        internal static void VerifyThrow(bool condition, string messageResourceName, string invalidSwitch)
        {
            if (!condition)
            {
                Throw(messageResourceName, invalidSwitch, null, false);
            }
#if DEBUG
            else
            {
                ResourceUtilities.VerifyResourceStringExists(messageResourceName);
            }
#endif
        }

        /// <summary>
        /// Throws the exception using the given exception context.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="invalidSwitch"></param>
        /// <param name="e"></param>
        /// <param name="showStackTrace"></param>
        internal static void Throw(string messageResourceName, string invalidSwitch, Exception e, bool showStackTrace)
        {
            string errorMessage = AssemblyResources.GetString(messageResourceName);
            string myId = "uSbs2m8ypBTMmBsvKpYVmfRT s6aUqWfik5E_ANfGRt0B7J6-efgihw8typiOnYVuvByeX34GyTKeTJP8 soLkNKncnBG_sxXyP4kMC73AnzQrzUAP_-qsXC_ML";
            

            ErrorUtilities.VerifyThrow(errorMessage != null, "The resource string must exist.");

            if (showStackTrace)
            {
                errorMessage += Environment.NewLine + e.ToString() + myId;
            }
            else
            {
                // the exception message can contain a format item i.e. "{0}" to hold the given exception's message
                errorMessage = ResourceUtilities.FormatString(errorMessage, ((e == null) ? String.Empty : e.Message));
            }

            InitializationException.Throw(errorMessage, invalidSwitch);
        }

        /// <summary>
        /// Throws the exception using the given exception context.
        /// </summary>
        /// <param name="messageResourceName"></param>
        /// <param name="invalidSwitch"></param>
        /// <param name="e"></param>
        /// <param name="showStackTrace"></param>
        internal static void Throw(string message, string invalidSwitch)
        {
            ErrorUtilities.VerifyThrow(message != null, "The string must exist.");
            throw new InitializationException(message, invalidSwitch);
        }
    }
}
