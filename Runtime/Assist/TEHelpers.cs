﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using TechTalk.SpecFlow.Assist.ValueRetrievers;
using System.Collections.Generic;

namespace TechTalk.SpecFlow.Assist
{
    internal static class TEHelpers
    {
        internal static T CreateTheInstanceWithTheDefaultConstructor<T>(Table table, Service service)
        {
            var instance = (T)Activator.CreateInstance(typeof(T));
            LoadInstanceWithKeyValuePairs(table, instance, service);
            return instance;
        }

        internal static T CreateTheInstanceWithTheValuesFromTheTable<T>(Table table, Service service)
        {
            var constructor = GetConstructorMatchingToColumnNames<T>(table);
            if (constructor == null)
                throw new MissingMethodException(string.Format("Unable to find a suitable constructor to create instance of {0}", typeof(T).Name));

            var membersThatNeedToBeSet = GetMembersThatNeedToBeSet(table, typeof(T), service);

            var constructorParameters = constructor.GetParameters();
            var parameterValues = new object[constructorParameters.Length];
            for (var parameterIndex = 0; parameterIndex < constructorParameters.Length; parameterIndex++)
            {
                var parameterName = constructorParameters[parameterIndex].Name;
                var member = (from m in membersThatNeedToBeSet
                                where m.MemberName == parameterName
                                select m).FirstOrDefault();
                if (member != null)
                    parameterValues[parameterIndex] = member.GetValue();
            }
            return (T)constructor.Invoke(parameterValues);
        }

        internal static bool ThisTypeHasADefaultConstructor<T>()
        {
            return typeof(T).GetConstructors()
                       .Where(c => c.GetParameters().Length == 0)
                       .Count() > 0;
        }

        internal static ConstructorInfo GetConstructorMatchingToColumnNames<T>(Table table)
        {
            var projectedPropertyNames = from property in typeof(T).GetProperties()
                                         from row in table.Rows
                                         where IsMemberMatchingToColumnName(property, row.Id())
                                         select property.Name;

            return (from constructor in typeof(T).GetConstructors()
                    where projectedPropertyNames.Except(
                        from parameter in constructor.GetParameters()
                        select parameter.Name).Count() == 0
                    select constructor).FirstOrDefault();
        }

        internal static bool IsMemberMatchingToColumnName(MemberInfo member, string columnName)
        {
            return member.Name.MatchesThisColumnName(columnName);
        }

        internal static bool MatchesThisColumnName(this string propertyName, string columnName)
        {
            var normalizedColumnName = RemoveAllCharactersThatAreNotValidInAPropertyName(columnName);
            var normalizedPropertyName = NormalizePropertyNameToMatchAgainstAColumnName(propertyName);

            return normalizedPropertyName.Equals(normalizedColumnName, StringComparison.OrdinalIgnoreCase);
        }

        internal static string RemoveAllCharactersThatAreNotValidInAPropertyName(string name)
        {
            return new Regex("[^a-zA-Z0-9_]").Replace(name, string.Empty);
        }

        internal static string NormalizePropertyNameToMatchAgainstAColumnName(string name)
        {
            return name.Replace("_", string.Empty);
        }

        internal static void LoadInstanceWithKeyValuePairs(Table table, object instance, Service service)
        {
            var membersThatNeedToBeSet = GetMembersThatNeedToBeSet(table, instance.GetType(), service);

            membersThatNeedToBeSet.ToList()
                .ForEach(x => x.Setter(instance, x.GetValue()));
        }

        internal static IEnumerable<MemberHandler> GetMembersThatNeedToBeSet(Table table, Type type, Service service)
        {
            var properties = from property in type.GetProperties()
                             from row in table.Rows
                             where TheseTypesMatch(property.PropertyType, row, service)
                                   && IsMemberMatchingToColumnName(property, row.Id())
                select new MemberHandler(service) { Type = type, Row = row, MemberName = property.Name, PropertyType = property.PropertyType, Setter = (i, v) => property.SetValue(i, v, null) };

            var fields = from field in type.GetFields()
                             from row in table.Rows
                             where TheseTypesMatch(field.FieldType, row, service)
                                   && IsMemberMatchingToColumnName(field, row.Id())
                select new MemberHandler(service) { Type = type, Row = row, MemberName = field.Name, PropertyType = field.FieldType, Setter = (i, v) => field.SetValue(i, v) };

            var memberHandlers = new List<MemberHandler>();

            memberHandlers.AddRange(properties);
            memberHandlers.AddRange(fields);

            return memberHandlers;
        }

        private static bool TheseTypesMatch(Type memberType, TableRow row, Service service)
        {
            return service.GetValueRetrieverFor(row, memberType) != null;
        }

        internal class MemberHandler
        {
            private readonly Service service;
            public TableRow Row { get; set; }
            public string MemberName { get; set; }
            public Action<object, object> Setter { get; set; }
            public Type Type { get; set; }
            public Type PropertyType { get; set; }

            public MemberHandler(Service service)
            {
                this.service = service;
            }

            public object GetValue()
            {
                var valueRetriever = service.GetValueRetrieverFor(Row, PropertyType);
                return valueRetriever.Retrieve(new KeyValuePair<string, string>(Row[0], Row[1]), Type);
            }
        }

        internal static Table GetTheProperInstanceTable(Table table, Type type)
        {
            return ThisIsAVerticalTable(table, type)
                       ? table
                       : FlipThisHorizontalTableToAVerticalTable(table);
        }

        private static Table FlipThisHorizontalTableToAVerticalTable(Table table)
        {
            return new PivotTable(table).GetInstanceTable(0);
        }

        private static bool ThisIsAVerticalTable(Table table, Type type)
        {
            if (TheHeaderIsTheOldFieldValuePair(table))
                return true;
            return (table.Rows.Count() != 1) || (table.Header.Count() == 2 && TheFirstRowValueIsTheNameOfAProperty(table, type));
        }

        private static bool TheHeaderIsTheOldFieldValuePair(Table table)
        {
            return table.Header.Count() == 2 && table.Header.First() == "Field" && table.Header.Last() == "Value";
        }

        private static bool TheFirstRowValueIsTheNameOfAProperty(Table table, Type type)
        {
            var firstRowValue = table.Rows[0][table.Header.First()];
            return type.GetProperties()
                .Any(property => IsMemberMatchingToColumnName(property, firstRowValue));
        }
    }
}
