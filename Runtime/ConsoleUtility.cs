﻿using System;
using System.Text;
using System.Text.RegularExpressions;

namespace BlackTundra.Foundation {

    public static class ConsoleUtility {

        #region constant

        private static readonly Regex XmlTagPattern = new Regex(@"<\/?[a-zA-Z0-9]{1}[^>]*>");

        #endregion

        #region logic

        #region UnknownArgumentMessage

        public static string UnknownArgumentMessage(in string arg) {

            if (arg == null) throw new ArgumentNullException("arg");
            return string.Format("Unknown argument: \"{0}\".", Escape(arg));

        }

        public static string UnknownArgumentMessage(in string[] args, int startIndex = 0, int length = 0) {

            if (args == null) throw new ArgumentNullException("args");
            if (args.Length == 0) throw new ArgumentException("args must have at least 1 element");
            if (args.Length == 1) return UnknownArgumentMessage(args[0]);

            if (length <= 0) length = args.Length;

            if (startIndex < 0) startIndex = 0;
            else if (startIndex >= length) startIndex = length - 1;
            
            StringBuilder messageBuilder = new StringBuilder(24 + (args.Length * 8));
            messageBuilder.Append("Unknown arguments: \"").Append(Escape(args[startIndex])).Append('\"');
            for (int i = startIndex + 1; i < length; i++) messageBuilder.Append(", \"").Append(Escape(args[i])).Append('\"');
            return messageBuilder.Append('.').ToString();

        }

        #endregion

        #region Escape

        public static string Escape(in string value) => value?.Replace("<", "<\b");

        #endregion

        #region RemoveFormatting

        public static string RemoveFormatting(in string value) => value != null ? XmlTagPattern.Replace(value, string.Empty) : string.Empty;

        #endregion

        #endregion

    }

}