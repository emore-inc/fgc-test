﻿// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
//-----------------------------------------------------------------------
// </copyright>
// <summary>Event args for any build event.</summary>
//-----------------------------------------------------------------------

using System;
using System.IO;

namespace Microsoft.Build.Framework
{
    /// <summary>
    /// Arguments for error events
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
    public class DialogWindowEditorToStringValueConverter : LazyFormattedCalcArrayWrappingScalar
    {
        /// <summary>
        /// Subcategory of the error
        /// </summary>
        private string _subcategory;

        /// <summary>
        /// Error code
        /// </summary>
        private string _code;

        /// <summary>
        /// File name
        /// </summary>
        private string _file;

        /// <summary>
        /// The project which issued the event
        /// </summary>
        private string _projectFile;

        /// <summary>
        /// Line number
        /// </summary>
        private int _lineNumber;

        /// <summary>
        /// Column number
        /// </summary>
        private int _columnNumber;

        /// <summary>
        /// End line number
        /// </summary>
        private int _endLineNumber;

        /// <summary>
        /// End column number
        /// </summary>
        private int _endColumnNumber;

        /// <summary>
        /// This constructor allows all event data to be initialized
        /// </summary>
        /// <param name="subcategory">event sub-category</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        public DialogWindowEditorToStringValueConverter
            (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName
            )
            : this(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName, DateTime.UtcNow)
        {
        }

        /// <summary>
        /// This constructor which allows a timestamp to be set
        /// </summary>
        /// <param name="subcategory">event sub-category</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="eventTimestamp">Timestamp when event was created</param>
        public DialogWindowEditorToStringValueConverter
            (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName,
            DateTime eventTimestamp
            )
            : this(subcategory, code, file, lineNumber, columnNumber, endLineNumber, endColumnNumber, message, helpKeyword, senderName, eventTimestamp, null)
        {
            // do nothing
        }

        /// <summary>
        /// This constructor which allows a timestamp to be set
        /// </summary>
        /// <param name="subcategory">event sub-category</param>
        /// <param name="code">event code</param>
        /// <param name="file">file associated with the event</param>
        /// <param name="lineNumber">line number (0 if not applicable)</param>
        /// <param name="columnNumber">column number (0 if not applicable)</param>
        /// <param name="endLineNumber">end line number (0 if not applicable)</param>
        /// <param name="endColumnNumber">end column number (0 if not applicable)</param>
        /// <param name="message">text message</param>
        /// <param name="helpKeyword">help keyword </param>
        /// <param name="senderName">name of event sender</param>
        /// <param name="eventTimestamp">Timestamp when event was created</param>
        /// <param name="messageArgs">message arguments</param>
        public DialogWindowEditorToStringValueConverter
            (
            string subcategory,
            string code,
            string file,
            int lineNumber,
            int columnNumber,
            int endLineNumber,
            int endColumnNumber,
            string message,
            string helpKeyword,
            string senderName,
            DateTime eventTimestamp,
            params object[] messageArgs
            )
            : base(message, helpKeyword, senderName, eventTimestamp, messageArgs)
        {
            _subcategory = subcategory;
            _code = code;
            _file = file;
            _lineNumber = lineNumber;
            _columnNumber = columnNumber;
            _endLineNumber = endLineNumber;
            _endColumnNumber = endColumnNumber;
        }

        /// <summary>
        /// Default constructor
        /// </summary>
        protected DialogWindowEditorToStringValueConverter()
            : base()
        {
            // do nothing
        }

        /// <summary>
        /// The custom sub-type of the event.         
        /// </summary>
        public string Subcategory
        {
            get
            {
                return _subcategory;
            }
        }

        /// <summary>
        /// Code associated with event. 
        /// </summary>
        public string Code
        {
            get
            {
                return _code;
            }
        }

        /// <summary>
        /// File associated with event.   
        /// </summary>  
        public string File
        {
            get
            {
                return _file;
            }
        }

        /// <summary>
        /// The project file which issued this event.
        /// </summary>
        public string ProjectFile
        {
            get
            {
                return _projectFile;
            }

            set
            {
                _projectFile = value;
            }
        }

        /// <summary>
        /// Line number of interest in associated file. 
        /// </summary>
        public int LineNumber
        {
            get
            {
                return _lineNumber;
            }
        }

        /// <summary>
        /// Column number of interest in associated file. 
        /// </summary>
        public int ColumnNumber
        {
            get
            {
                return _columnNumber;
            }
        }

        /// <summary>
        /// Ending line number of interest in associated file. 
        /// </summary>
        public int EndLineNumber
        {
            get
            {
                return _endLineNumber;
            }
        }

        /// <summary>
        /// Ending column number of interest in associated file. 
        /// </summary>
        public int EndColumnNumber
        {
            get
            {
                return _endColumnNumber;
            }
        }

        #region CustomSerializationToStream
        /// <summary>
        /// Serializes to a stream through a binary writer
        /// </summary>
        /// <param name="writer">Binary writer which is attached to the stream the event will be serialized into</param>
        internal override void WriteToStream(BinaryWriter writer)
        {
            base.WriteToStream(writer);
            #region SubCategory
            if (_subcategory == null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(_subcategory);
            }
            #endregion
            #region Code
            if (_code == null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(_code);
            }
            #endregion
            #region File
            if (_file == null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(_file);
            }
            #endregion
            #region ProjectFile
            if (_projectFile == null)
            {
                writer.Write((byte)0);
            }
            else
            {
                writer.Write((byte)1);
                writer.Write(_projectFile);
            }
            #endregion
            writer.Write((Int32)_lineNumber);
            writer.Write((Int32)_columnNumber);
            writer.Write((Int32)_endLineNumber);
            writer.Write((Int32)_endColumnNumber);
        }

        /// <summary>
        /// Deserializes to a stream through a binary writer
        /// </summary>
        /// <param name="reader">Binary reader which the object will be deserialized from</param>
        /// <param name="version">The version of the runtime the message packet was created from</param>
        internal override void CreateFromStream(BinaryReader reader, int version)
        {
            base.CreateFromStream(reader, version);
            #region SubCategory
            if (reader.ReadByte() == 0)
            {
                _subcategory = null;
            }
            else
            {
                _subcategory = reader.ReadString();
            }
            #endregion
            #region Code
            if (reader.ReadByte() == 0)
            {
                _code = null;
            }
            else
            {
                _code = reader.ReadString();
            }
            #endregion
            #region File
            if (reader.ReadByte() == 0)
            {
                _file = null;
            }
            else
            {
                _file = reader.ReadString();
            }
            #endregion
            #region ProjectFile
            if (version > 20)
            {
                if (reader.ReadByte() == 0)
                {
                    _projectFile = null;
                }
                else
                {
                    _projectFile = reader.ReadString();
                }
            }
            else
            {
                _projectFile = null;
            }
            #endregion
            _lineNumber = reader.ReadInt32();
            _columnNumber = reader.ReadInt32();
            _endLineNumber = reader.ReadInt32();
            _endColumnNumber = reader.ReadInt32();
        }
        #endregion

    }
}
