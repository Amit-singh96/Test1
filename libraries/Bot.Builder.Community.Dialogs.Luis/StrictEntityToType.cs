namespace Bot.Builder.Community.Dialogs.Luis
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using System.Linq;
    using Bot.Builder.Community.Dialogs.Luis.Models;
    using static Bot.Builder.Community.Dialogs.Luis.BuiltIn.DateTime;

    public sealed class StrictEntityToType : IEntityToType
    {
        private readonly IResolutionParser parser;
        private readonly ICalendarPlus calendar;

        public StrictEntityToType(IResolutionParser parser, ICalendarPlus calendar)
        {
            SetField.NotNull(out this.parser, nameof(parser), parser);
            SetField.NotNull(out this.calendar, nameof(calendar), calendar);
        }

        /// <summary>
        /// Interpret a parsed DateTimeResolution to provide a series of DateTime ranges
        /// </summary>
        /// <param name="resolution">The DateTimeResolution parsed from a LUIS response.</param>
        /// <param name="now">The reference point of "now".</param>
        /// <param name="calendar">The calendar to use for date math.</param>
        /// <param name="rule">The calendar week rule to use for date math.</param>
        /// <param name="firstDayOfWeek">The first day of the week to use for date math.</param>
        /// <param name="hourFor">The hour that corresponds to the <see cref="DayPart"/> enumeration.</param>
        /// <returns>A potentially infinite series of DateTime ranges.</returns>
        public static IEnumerable<Range<DateTime>> Interpret(DateTimeResolution resolution, DateTime now, Calendar calendar, CalendarWeekRule rule, DayOfWeek firstDayOfWeek, Func<DayPart, int> hourFor)
        {
            // remove any millisecond components
            now = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, now.Second, now.Kind);

            switch (resolution.Reference)
            {
                case Reference.PAST_REF:
                    yield return Range.From(DateTime.MinValue, now);
                    yield break;
                case Reference.PRESENT_REF:
                    yield return Range.From(now, now);
                    yield break;
                case Reference.FUTURE_REF:
                    yield return Range.From(now, DateTime.MaxValue);
                    yield break;
                case null:
                    break;
                default:
                    throw new NotImplementedException();
            }

            var start = now;

            // TODO: maybe clamp to prevent divergence
            while (start < DateTime.MaxValue)
            {
                var after = start;

                while (true)
                {
                    // for each date component in decreasing order of significance:
                    // if it's not a variable (-1) or missing (null) component, then
                    //      add a unit of that component to "start"
                    //      round down to the component's granularity
                    //      calculate the "after" based on the size of that component
                    if (resolution.Year >= 0)
                    {
                        bool need = start.Year != resolution.Year;
                        if (need)
                        {
                            start = start.AddYears(1);
                            start = new DateTime(start.Year, 1, 1, 0, 0, 0, 0, start.Kind);
                        }

                        if (start.Year > resolution.Year)
                        {
                            yield break;
                        }

                        after = start.AddYears(1);

                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.Month >= 0)
                    {
                        bool need = start.Month != resolution.Month;
                        if (need)
                        {
                            start = start.AddMonths(1);
                            start = new DateTime(start.Year, start.Month, 1, 0, 0, 0, 0, start.Kind);
                        }

                        after = start.AddMonths(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    var week = calendar.GetWeekOfYear(start, rule, firstDayOfWeek);
                    if (resolution.Week >= 0)
                    {
                        bool need = week != resolution.Week;
                        if (need)
                        {
                            start = start.AddDays(7);
                            start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);

                            while (start.DayOfWeek != firstDayOfWeek)
                            {
                                start = start.AddDays(-1);
                            }
                        }

                        after = start.AddDays(7);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.DayOfWeek != null)
                    {
                        bool need = start.DayOfWeek != resolution.DayOfWeek;
                        if (need)
                        {
                            start = start.AddDays(1);
                            start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);
                        }

                        after = start.AddDays(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.Day >= 0)
                    {
                        bool need = start.Day != resolution.Day;
                        if (need)
                        {
                            start = start.AddDays(1);
                            start = new DateTime(start.Year, start.Month, start.Day, 0, 0, 0, 0, start.Kind);
                        }

                        after = start.AddDays(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.DayPart != null)
                    {
                        var hourStart = hourFor(resolution.DayPart.Value);
                        var hourAfter = hourFor(resolution.DayPart.Value.Next());
                        var hourDelta = hourAfter - hourStart;
                        if (hourDelta < 0)
                        {
                            hourDelta += 24;
                        }

                        bool need = start.Hour != hourStart;
                        if (need)
                        {
                            start = start.AddHours(1);
                            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, 0, start.Kind);
                        }

                        after = start.AddHours(hourDelta);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.Hour >= 0)
                    {
                        bool need = start.Hour != resolution.Hour;
                        if (need)
                        {
                            start = start.AddHours(1);
                            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, 0, 0, 0, start.Kind);
                        }

                        after = start.AddHours(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.Minute >= 0)
                    {
                        bool need = start.Minute != resolution.Minute;
                        if (need)
                        {
                            start = start.AddMinutes(1);
                            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, 0, 0, start.Kind);
                        }

                        after = start.AddMinutes(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    if (resolution.Second >= 0)
                    {
                        bool need = start.Second != resolution.Second;
                        if (need)
                        {
                            start = start.AddSeconds(1);
                            start = new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second, 0, start.Kind);
                        }

                        after = start.AddSeconds(1);
                        if (need)
                        {
                            continue;
                        }
                    }

                    // if all of the components were variable or missing,
                    // then in order of increasing component granularity,
                    // if the component is variable rather than missing, then increment by that granularity
                    if (start == after)
                    {
                        if (resolution.Second < 0)
                        {
                            after = start.AddSeconds(1);
                        }
                        else if (resolution.Minute < 0)
                        {
                            after = start.AddMinutes(1);
                        }
                        else if (resolution.Hour < 0)
                        {
                            after = start.AddHours(1);
                        }
                        else if (resolution.Day < 0)
                        {
                            after = start.AddDays(1);
                        }
                        else if (resolution.Week < 0)
                        {
                            after = start.AddDays(7);
                        }
                        else if (resolution.Month < 0)
                        {
                            after = start.AddMonths(1);
                        }
                        else if (resolution.Year < 0)
                        {
                            after = start.AddYears(1);
                        }
                        else
                        {
                            // a second is our minimum granularity
                            after = start.AddSeconds(1);
                        }
                    }

                    if (start >= now)
                    {
                        yield return new Range<DateTime>(start, after);
                    }

                    start = after;
                }
            }
        }

        bool IEntityToType.TryMapToDateRanges(DateTime now, IEnumerable<EntityRecommendation> entities, out IEnumerable<Range<DateTime>> ranges)
        {
            var resolutions = this.parser.ParseResolutions(entities);
            var dateTimes = resolutions.OfType<DateTimeResolution>().ToArray();

            if (dateTimes.Length > 0)
            {
                // possibly infinite
                var merged = dateTimes
                    .Select(r => Interpret(r, now, this.calendar.Calendar, this.calendar.WeekRule, this.calendar.FirstDayOfWeek, this.calendar.HourFor))
                    .Aggregate((l, r) => l.SortedMerge(r));

                ranges = merged;
                return true;
            }
            else
            {
                ranges = null;
                return false;
            }
        }

        bool IEntityToType.TryMapToTimeSpan(DateTime now, IEnumerable<EntityRecommendation> entities, out TimeSpan span)
        {
            span = default(TimeSpan);
            return false;
        }
    }
}
