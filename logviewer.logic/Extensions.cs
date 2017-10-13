﻿// This is an open source non-commercial project. Dear PVS-Studio, please check it.
// PVS-Studio Static Code Analyzer for C, C++ and C#: http://www.viva64.com
// Created by: egr
// Created at: 20.09.2012
// © 2012-2017 Alexander Egorov

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Threading;
using EnumsNET;
using Humanizer;
using logviewer.engine;
using logviewer.logic.Annotations;
using logviewer.logic.Properties;
using logviewer.logic.support;

namespace logviewer.logic
{
    public static class Extensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        [Pure]
        internal static string ToParameterName(this LogLevel level) => level.GetName() + @"Color";

        [Pure]
        internal static bool HasProperty(this ICollection<Semantic> schema, ParserType type) => schema.FilterSchema(type).Any();

        [Pure]
        internal static string PropertyNameOf(this ICollection<Semantic> schema, ParserType type) => schema
                .FilterSchema(type).Select(s => s.Property).FirstOrDefault();

        private static IEnumerable<Semantic> FilterSchema(this IEnumerable<Semantic> schema, ParserType type) => from s in schema
                                                                                                                 from rule in s.CastingRules
                                                                                                                 where rule.Type == type
                                                                                                                 select s;

        /// <summary>
        /// Whether the string is valid to use as text filter
        /// </summary>
        /// <param name="messageTextFilter">string that supposed to be a filter</param>
        /// <param name="useRegularExpressions">Wheter to use regular expressions in filter string</param>
        /// <returns></returns>
        [Pure]
        public static bool IsValid(this string messageTextFilter, bool useRegularExpressions)
        {
            if (string.IsNullOrEmpty(messageTextFilter) || !useRegularExpressions)
            {
                return true;
            }

            try
            {
                var r = new Regex(messageTextFilter, RegexOptions.IgnoreCase | RegexOptions.Singleline);
                return r.GetHashCode() > 0;
            }
            catch (Exception e)
            {
                Log.Instance.Info(e.Message, e);
                return false;
            }
        }

        [Pure]
        public static int ToSafePercent(this int value, int min, int max)
        {
            if (value > max)
            {
                return max;
            }

            return value < min ? min : value;
        }

        [Pure]
        private static string BytesToString(this ulong bytes) => ((long)bytes).Declension(Resources.BytesNominative,
                                                                                          Resources.BytesGenitiveSingular,
                                                                                          Resources.BytesGenitivePlural);

        [Pure]
        public static string Format(this LoadProgress progress)
        {
            return progress.Speed.Bytes == 0
                       ? string.Format(Resources.SpeedPercent, progress.Percent)
                       : string.Format(Resources.SpeedPercentWithRemain, progress.Percent, progress.Speed.Format(),
                                       progress.Remainig.Humanize());
        }

        /// <summary>
        /// If the file can be opened for exclusive access it means that the file
        /// is no longer locked by another process.
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        public static bool IsFileReady(this string path)
        {
            try
            {
                using (var inputStream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None))
                {
                    return inputStream.Length > 0;
                }
            }
            catch (Exception e)
            {
                Log.Instance.Debug(e);
                return false;
            }
        }

        private const string BigFileFormatNoBytes = @"{0:F2} {1}";

        private const string SmallFileFormat = @"{0} {1}";

        private static readonly string[] sizes =
        {
            Resources.SizeBytes,
            Resources.SizeKBytes,
            Resources.SizeMBytes,
            Resources.SizeGBytes,
            Resources.SizeTBytes,
            Resources.SizePBytes,
            Resources.SizeEBytes
        };

        [Pure]
        public static string Format(this FileSize fileSize)
        {
            if (fileSize.Unit == SizeUnit.Bytes)
            {
                return string.Format(CultureInfo.CurrentCulture, SmallFileFormat, fileSize.Bytes,
                                     fileSize.Bytes.BytesToString());
            }

            var noBytes = string.Format(CultureInfo.CurrentCulture,
                                        BigFileFormatNoBytes,
                                        fileSize.Value,
                                        sizes[(int)fileSize.Unit]);
            if (fileSize.BigWithoutBytes)
            {
                return noBytes;
            }

            return noBytes + $" ({fileSize.Bytes.ToString(@"N0", CultureInfo.CurrentCulture)} {fileSize.Bytes.BytesToString()})";
        }

        private static readonly IDictionary<int, Func<long, string, string, string, string>> declensions =
                new Dictionary<int, Func<long, string, string, string, string>>
                {
                    {
                        1049,
                        DeclensionRu
                    }
                };

        private static string Declension(this long number, string nominative, string genitiveSingular,
                                         string genitivePlural)
        {
            return declensions.ContainsKey(Thread.CurrentThread.CurrentUICulture.LCID)
                       ? declensions[Thread.CurrentThread.CurrentUICulture.LCID](number, nominative, genitiveSingular, genitivePlural)
                       : DeclensionEn(number, nominative, genitiveSingular, genitivePlural);
        }

        /// <summary>
        /// Does a word declension after a number.
        /// </summary>
        /// <param name="number"></param>
        /// <param name="nominative"></param>
        /// <param name="genitiveSingular"></param>
        /// <param name="genitivePlural"></param>
        /// <returns></returns>
        private static string DeclensionRu(long number, string nominative, string genitiveSingular, string genitivePlural)
        {
            var lastDigit = number % 10;
            var lastTwoDigits = number % 100;
            switch (lastDigit)
            {
                case 1 when lastTwoDigits != 11:
                    return nominative;
                case 2 when lastTwoDigits != 12:
                case 3 when lastTwoDigits != 13:
                case 4 when lastTwoDigits != 14:
                    return genitiveSingular;
            }

            return genitivePlural;
        }

        /// <summary>
        /// Does a word declension after a number.
        /// </summary>
        /// <param name="number"></param>
        /// <param name="nominative"></param>
        /// <param name="genitiveSingular"></param>
        /// <param name="genitivePlural"></param>
        /// <returns></returns>
        private static string DeclensionEn(long number, string nominative, string genitiveSingular, string genitivePlural)
        {
            if (number == 1 || number == -1)
            {
                return nominative;
            }

            return genitiveSingular ?? genitivePlural;
        }
    }
}
