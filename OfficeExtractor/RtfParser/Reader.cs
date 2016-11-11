﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

/*
   Copyright 2013 - 2016 Kees van Spelde

   Licensed under The Code Project Open License (CPOL) 1.02;
   you may not use this file except in compliance with the License.
   You may obtain a copy of the License at

     http://www.codeproject.com/info/cpol10.aspx

   Unless required by applicable law or agreed to in writing, software
   distributed under the License is distributed on an "AS IS" BASIS,
   WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
   See the License for the specific language governing permissions and
   limitations under the License.
*/

namespace OfficeExtractor.RtfParser
{
    /// <summary>
    /// Used to read from an RTF file
    /// </summary>
    internal class Reader
    {
        #region RtfParseState
        private enum RtfParseState
        {
            /// <summary>
            /// The token in a control word
            /// </summary>
            ControlWord,

            /// <summary>
            /// The token contains text
            /// </summary>
            Text,

            /// <summary>
            /// The token contains escaped text
            /// </summary>
            EscapedText,

            /// <summary>
            /// The token is the start of a group
            /// </summary>
            Group
        }
        #endregion

        #region Fields
        /// <summary>
        /// Used to read the RTF file
        /// </summary>
        public TextReader TextReader { get; private set; }
        #endregion

        #region Constructor
        /// <summary>
        /// Class used to read RTF files
        /// </summary>
        /// <param name="reader"></param>
        public Reader(TextReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException("reader");

            TextReader = reader;
        }
        #endregion

        #region Read
        /// <summary>
        /// Reads all the tokens from the RTF file and returns it as a IEnumerable list of objects
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Object> Read()
        {
            var controlWord = new StringBuilder();
            var text = new StringBuilder();
            var stack = new Stack<RtfParseState>();
            var state = RtfParseState.Group;

            do
            {
                var i = TextReader.Read();
                if (i < 0)
                {
                    if (!string.IsNullOrWhiteSpace(controlWord.ToString()))
                        yield return new ControlWord(controlWord.ToString());

                    if (!string.IsNullOrWhiteSpace(text.ToString()))
                        yield return new Text(text.ToString());

                    yield break;
                }

                var c = (char) i;

                if ((c == '\r') ||
                    (c == '\n'))
                    continue;

                switch (state)
                {
                    case RtfParseState.Group:
                        if (c == '{')
                        {
                            stack.Push(state);
                            break;
                        }

                        if (c == '\\')
                            state = RtfParseState.ControlWord;

                            break;

                    case RtfParseState.ControlWord:
                        if (c == '\\')
                        {
                            // Another controlWord
                            if (!string.IsNullOrWhiteSpace(controlWord.ToString()))
                            {
                                yield return new ControlWord(controlWord.ToString());
                                controlWord.Clear();
                            }
                            break;
                        }

                        if (c == '{')
                        {
                            // New group
                            state = RtfParseState.Group;
                            if (!string.IsNullOrWhiteSpace(controlWord.ToString()))
                            {
                                yield return new ControlWord(controlWord.ToString());
                                controlWord.Clear();
                            }
                            break;
                        }

                        if (c == '}')
                        {
                            // Close group
                            state = stack.Count > 0 ? stack.Pop() : RtfParseState.Group;
                            if (!string.IsNullOrWhiteSpace(controlWord.ToString()))
                            {
                                yield return new ControlWord(controlWord.ToString());
                                controlWord.Clear();
                            }
                            break;
                        }

                        if (!Char.IsLetterOrDigit(c))
                        {
                            state = RtfParseState.Text;
                            text.Append(c);
                            if (!string.IsNullOrWhiteSpace(controlWord.ToString()))
                            {
                                yield return new ControlWord(controlWord.ToString());
                                controlWord.Clear();
                            }
                            break;
                        }

                        controlWord.Append(c);
                        break;

                    case RtfParseState.Text:
                        if (c == '\\')
                        {
                            state = RtfParseState.EscapedText;
                            break;
                        }

                        if (c == '{')
                        {
                            if (!string.IsNullOrWhiteSpace(text.ToString()))
                            {
                                yield return new Text(text.ToString());
                                text.Clear();
                            }

                            // a new group
                            state = RtfParseState.Group;
                            break;
                        }

                        if (c == '}')
                        {
                            if (!string.IsNullOrWhiteSpace(text.ToString()))
                            {
                                yield return new Text(text.ToString());
                                text.Clear();
                            }

                            // close group
                            state = stack.Count > 0 ? stack.Pop() : RtfParseState.Group;
                            break;
                        }
                        text.Append(c);
                        break;

                    case RtfParseState.EscapedText:
                        if ((c == '\\') || (c == '}') || (c == '{'))
                        {
                            state = RtfParseState.Text;
                            text.Append(c);
                            break;
                        }

                        // Ansi character escape
                        if (c == '\'')
                        {
                            text.Append(FromHexa((char)TextReader.Read(), (char)TextReader.Read()));
                            break;
                        }

                        if (!string.IsNullOrWhiteSpace(text.ToString()))
                        {
                            yield return new Text(text.ToString());
                            text.Clear();
                        }

                        // Normal control word
                        controlWord.Append(c);
                        state = RtfParseState.ControlWord;
                        break;
                }
            }
            while (true);
        }
        #endregion

        #region MoveToNextControlWord
        /// <summary>
        /// Advances the parser to the next control word
        /// </summary>
        /// <param name="enumerator"></param>
        /// <param name="word"></param>
        /// <returns></returns>
        public static bool MoveToNextControlWord(IEnumerator<Object> enumerator, string word)
        {
            if (enumerator == null)
                throw new ArgumentNullException("enumerator");

            while (enumerator.MoveNext())
            {
                if (enumerator.Current.Text == word)
                    return true;
            }
            return false;
        }
        #endregion

        #region GetNextText
        /// <summary>
        /// Returns the next text block in the RTF file, null is returned when no more text is found
        /// </summary>
        /// <param name="enumerator"></param>
        /// <returns></returns>
        public static string GetNextText(IEnumerator<Object> enumerator)
        {
            if (enumerator == null)
                throw new ArgumentNullException("enumerator");

            while (enumerator.MoveNext())
            {
                var rtfText = enumerator.Current as Text;
                if (rtfText != null)
                    return rtfText.Text;
            }

            return null;
        }
        #endregion

        #region GetNextTextAsByteArray
        /// <summary>
        /// Returns the next text block in the RTF file as a byte array, null is returned when no more text is found
        /// </summary>
        /// <param name="enumerator"></param>
        /// <returns></returns>
        public static byte[] GetNextTextAsByteArray(IEnumerator<Object> enumerator)
        {
            if (enumerator == null)
                throw new ArgumentNullException("enumerator");

            while (enumerator.MoveNext())
            {
                var rtfText = enumerator.Current as Text;
                if (rtfText == null) continue;
                var bytes = new List<byte>();

                for (var i = 0; i < rtfText.Text.Length; i += 2)
                    bytes.Add((byte) FromHexa(rtfText.Text[i], rtfText.Text[i + 1]));

                return bytes.ToArray();
            }
            return null;
        }
        #endregion

        #region FromHexa
        /// <summary>
        /// Converts a hexadecimal notation to a char
        /// </summary>
        /// <param name="hi"></param>
        /// <param name="lo"></param>
        /// <returns></returns>
        private static char FromHexa(char hi, char lo)
        {
            return (char) byte.Parse(hi.ToString() + lo, NumberStyles.HexNumber);
        }
        #endregion
    }
}