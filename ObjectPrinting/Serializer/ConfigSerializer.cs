﻿using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using ObjectPrinting.Serializer.Configs;
using ObjectPrinting.Tools;

namespace ObjectPrinting.Serializer;

public class ConfigSerializer<TOwner>(PrintingConfig<TOwner> config)
{
    public string PrintToString(object? obj, uint nestingLevel)
    {
        if (obj is null)
            return $"null{Environment.NewLine}";
        
        if (TryNonNestingSerialization(obj) is { } serializedObject)
            return serializedObject;
        
        if (nestingLevel + 1 > config.MaxNestingLevel)
            return "..." + Environment.NewLine;
        
        if (obj is IEnumerable enumerable)
            return enumerable is IDictionary dict 
                ? GetPrintedDictionary(dict, nestingLevel) 
                : GetPrintedSequence(enumerable, nestingLevel);
        
        var type = obj.GetType();
        var sb = new StringBuilder(type.Name + Environment.NewLine);
        var indentation = new string('\t', (int)(nestingLevel + 1));
        
        foreach (var memberInfo in type.GetMembers().Where(info => info.IsPropertyOrField()))
        {
            if (config.ExcludedTypes.Contains(memberInfo.TryGetType()) ||
                config.ExcludedProperties.Contains(memberInfo.TryGetFullName()))
                continue;
            
            var printingResult = 
                TryCustomSerialization(obj, memberInfo) ?? 
                PrintToString(TryGetMemberValue(memberInfo, obj), nestingLevel + 1);
            sb.Append(indentation + memberInfo.Name + " = " + TrimIfNeeded(memberInfo, printingResult));
        }
        return sb.ToString();
    }
    
    private string GetPrintedSequence(IEnumerable obj, uint nestingLevel)
    {
        var index = 0;
        var sb = new StringBuilder("[");
        var indentation = new string('\t', (int)(nestingLevel + 1));

        foreach (var element in obj)
        {
            var serializedElement = PrintToString(element, nestingLevel + 1);
            if (sb.Length == 1) sb.Append(Environment.NewLine);
            sb.Append($"{indentation}{index++}: {serializedElement}");
        }
        
        var arrayClosing = sb.Length == 1 
            ? string.Empty 
            : new string('\t', (int)nestingLevel);
        return sb + arrayClosing + "]" + Environment.NewLine;
    }

    private string GetPrintedDictionary(IDictionary dict, uint nestingLevel)
    {
        var sb = new StringBuilder("{");
        var indentation = new string('\t', (int)(nestingLevel + 1));

        foreach (DictionaryEntry pair in dict)
        {
            var serializedElement = PrintToString(pair.Value, nestingLevel + 1);
            if (sb.Length == 1) sb.Append(Environment.NewLine);
            sb.Append($"{indentation}{pair.Key}: {serializedElement}");
        }

        var dictClosing = sb.Length == 1
            ? string.Empty
            : new string('\t', (int)nestingLevel);
        return sb + dictClosing + "}" + Environment.NewLine;
    }

    private string? TryNonNestingSerialization(object obj)
    {
        if (config.CulturesForTypes.TryGetValue(obj.GetType(), out var culture))
            return ((IFormattable)obj).ToString(null, culture) + Environment.NewLine;
        
        if (obj is string str && config.TrimStringValue is { } trimmed)
            return TrimIfNeeded(str + Environment.NewLine, (int)trimmed);
        
        return IsFinalType(obj.GetType()) ? obj + Environment.NewLine : null;
    }

    private string? TryCustomSerialization(object obj, MemberInfo memberInfo)
    {
        var value = TryGetMemberValue(memberInfo, obj);
        if (value is null) return "null" + Environment.NewLine;
        
        if (config.TypeSerializers.TryGetValue(memberInfo.TryGetType(), out var typeSerializer))
            return TrimIfNeeded(memberInfo, typeSerializer(value) + Environment.NewLine);
        
        if (config.PropertySerializers.TryGetValue(memberInfo.TryGetFullName(), out var propertySerializer))
            return TrimIfNeeded(memberInfo, propertySerializer(value) + Environment.NewLine);

        return null;
    }
    
    private string TrimIfNeeded(MemberInfo memberInfo, string printingResult) 
        => config.TrimmedMembers.TryGetValue(memberInfo.TryGetFullName(), out var length) 
            ? TrimIfNeeded(printingResult, (int)length) : printingResult;
    
    private static string TrimIfNeeded(string str, int length) 
        => str.Length >= length ? str[..length] + Environment.NewLine : str;
    
    private static bool IsFinalType(Type type)
        => type.IsPrimitive || type == typeof(string) || type == typeof(Guid) || type == typeof(DateTime);
    
    private static object? TryGetMemberValue(MemberInfo member, object obj)
    {
        if (!member.IsPropertyOrField())
            throw new ArgumentException("Provided member must be Field or Property");

        return member.MemberType == MemberTypes.Field
            ? ((FieldInfo)member).GetValue(obj) : ((PropertyInfo)member).GetValue(obj);
    }
}