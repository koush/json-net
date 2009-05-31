﻿#region License
// Copyright (c) 2007 James Newton-King
//
// Permission is hereby granted, free of charge, to any person
// obtaining a copy of this software and associated documentation
// files (the "Software"), to deal in the Software without
// restriction, including without limitation the rights to use,
// copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following
// conditions:
//
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES
// OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY,
// WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR
// OTHER DEALINGS IN THE SOFTWARE.
#endregion

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Utilities;

namespace Newtonsoft.Json.Serialization
{
  internal class JsonSerializerReader
  {
    internal readonly JsonSerializer _serializer;
    private JsonSerializerProxy _internalSerializer;

    public JsonSerializerReader(JsonSerializer serializer)
    {
      ValidationUtils.ArgumentNotNull(serializer, "serializer");

      _serializer = serializer;
    }

    public void Populate(JsonReader reader, object target)
    {
      ValidationUtils.ArgumentNotNull(target, "target");

      Type objectType = target.GetType();

      if (reader.TokenType == JsonToken.None)
        reader.Read();

      if (reader.TokenType == JsonToken.StartArray)
      {
        PopulateList(CollectionUtils.CreateCollectionWrapper(target), ReflectionUtils.GetCollectionItemType(objectType), reader, null);
      }
      else if (reader.TokenType == JsonToken.StartObject)
      {
        CheckedRead(reader);

        string id = null;
        if (reader.TokenType == JsonToken.PropertyName && string.Equals(reader.Value.ToString(), JsonTypeReflector.IdPropertyName, StringComparison.Ordinal))
        {
          CheckedRead(reader);
          id = reader.Value.ToString();
          CheckedRead(reader);
        }

        if (CollectionUtils.IsDictionaryType(objectType))
          PopulateDictionary(CollectionUtils.CreateDictionaryWrapper(target), reader, id);
        else
          PopulateObject(target, reader, objectType, id);
      }
    }

    public object Deserialize(JsonReader reader, Type objectType)
    {
      if (reader == null)
        throw new ArgumentNullException("reader");

      if (!reader.Read())
        return null;

      return CreateValue(reader, objectType, null, null);
    }

    private JsonSerializerProxy GetInternalSerializer()
    {
      if (_internalSerializer == null)
        _internalSerializer = new JsonSerializerProxy(this);

      return _internalSerializer;
    }

    private JToken CreateJToken(JsonReader reader)
    {
      ValidationUtils.ArgumentNotNull(reader, "reader");

      JToken token;
      using (JTokenWriter writer = new JTokenWriter())
      {
        writer.WriteToken(reader);
        token = writer.Token;
      }

      return token;
    }

    private JToken CreateJObject(JsonReader reader)
    {
      ValidationUtils.ArgumentNotNull(reader, "reader");

//          throw new Exception("Expected current token of type {0}, got {1}.".FormatWith(CultureInfo.InvariantCulture, JsonToken.PropertyName, reader.TokenType));

      JToken token;
      using (JTokenWriter writer = new JTokenWriter())
      {
        writer.WriteStartObject();

        if (reader.TokenType == JsonToken.PropertyName)
          writer.WriteToken(reader, reader.Depth - 1);
        else
          writer.WriteEndObject();

        token = writer.Token;
      }

      return token;
    }

    private object CreateValue(JsonReader reader, Type objectType, object existingValue, JsonConverter memberConverter)
    {
      JsonConverter converter;

      if (memberConverter != null)
      {
        return memberConverter.ReadJson(reader, objectType, GetInternalSerializer());
      }
      else if (objectType != null && _serializer.HasClassConverter(objectType, out converter))
      {
        return converter.ReadJson(reader, objectType, GetInternalSerializer());
      }
      else if (objectType != null && _serializer.HasMatchingConverter(objectType, out converter))
      {
        return converter.ReadJson(reader, objectType, GetInternalSerializer());
      }
      else if (objectType == typeof(JsonRaw))
      {
        return JsonRaw.Create(reader);
      }
      else
      {
        do
        {
          switch (reader.TokenType)
          {
            // populate a typed object or generic dictionary/array
            // depending upon whether an objectType was supplied
            case JsonToken.StartObject:
              CheckedRead(reader);

              string id = null;

              if (reader.TokenType == JsonToken.PropertyName)
              {
                bool specialProperty;

                do
                {
                  string propertyName = reader.Value.ToString();

                  if (string.Equals(propertyName, JsonTypeReflector.RefPropertyName, StringComparison.Ordinal))
                  {
                    CheckedRead(reader);
                    string reference = reader.Value.ToString();

                    CheckedRead(reader);
                    return _serializer.ReferenceResolver.ResolveReference(reference);
                  }
                  else if (string.Equals(propertyName, JsonTypeReflector.TypePropertyName, StringComparison.Ordinal))
                  {
                    CheckedRead(reader);
                    string qualifiedTypeName = reader.Value.ToString();

                    CheckedRead(reader);

                    if (_serializer.TypeNameHandling != TypeNameHandling.None)
                    {
                      int delimiterIndex = qualifiedTypeName.IndexOf(',');
                      string typeName;
                      string assemblyName;
                      if (delimiterIndex != -1)
                      {
                        typeName = qualifiedTypeName.Substring(0, delimiterIndex).Trim();
                        assemblyName = qualifiedTypeName.Substring(delimiterIndex + 1, qualifiedTypeName.Length - delimiterIndex - 1).Trim();
                      }
                      else
                      {
                        typeName = qualifiedTypeName;
                        assemblyName = null;
                      }

                      Type specifiedType;
                      try
                      {
                        specifiedType = _serializer.Binder.BindToType(assemblyName, typeName);
                      }
                      catch (Exception ex)
                      {
                        throw new JsonSerializationException("Error resolving type specified in JSON '{0}'.".FormatWith(CultureInfo.InvariantCulture, qualifiedTypeName), ex);
                      }

                      if (specifiedType == null)
                        throw new JsonSerializationException("Type specified in JSON '{0}' was not resolved.".FormatWith(CultureInfo.InvariantCulture, qualifiedTypeName));

                      if (objectType != null && !objectType.IsAssignableFrom(specifiedType))
                        throw new JsonSerializationException("Type specified in JSON '{0}' is not compatible with '{1}'.".FormatWith(CultureInfo.InvariantCulture, specifiedType.AssemblyQualifiedName, objectType.AssemblyQualifiedName));

                      objectType = specifiedType;
                    }
                    specialProperty = true;
                  }
                  else if (string.Equals(propertyName, JsonTypeReflector.IdPropertyName, StringComparison.Ordinal))
                  {
                    CheckedRead(reader);

                    id = reader.Value.ToString();
                    CheckedRead(reader);
                    specialProperty = true;
                  }
                  else if (string.Equals(propertyName, JsonTypeReflector.ArrayValuesPropertyName, StringComparison.Ordinal))
                  {
                    CheckedRead(reader);
                    object list = CreateList(reader, objectType, existingValue, id);
                    CheckedRead(reader);
                    return list;
                  }
                  else
                  {
                    specialProperty = false;
                  }
                } while (specialProperty
                         && reader.TokenType == JsonToken.PropertyName);
              }

              if (objectType == null)
              {
                return CreateJObject(reader);
              }
              else
              {
                if (CollectionUtils.IsDictionaryType(objectType))
                {
                  if (existingValue == null)
                    return CreateAndPopulateDictionary(reader, objectType, id);
                  else
                    return PopulateDictionary(CollectionUtils.CreateDictionaryWrapper(existingValue), reader, id);
                }
                else
                {
                  if (existingValue == null)
                    return CreateAndPopulateObject(reader, objectType, id);
                  else
                    return PopulateObject(existingValue, reader, objectType, id);
                }
              }
              break;
            case JsonToken.StartArray:
              return CreateList(reader, objectType, existingValue, null);
              break;
            case JsonToken.Integer:
            case JsonToken.Float:
            case JsonToken.String:
            case JsonToken.Boolean:
            case JsonToken.Date:
              return EnsureType(reader.Value, objectType);
              break;
            case JsonToken.StartConstructor:
            case JsonToken.EndConstructor:
              string constructorName = reader.Value.ToString();

              return constructorName;
              break;
            case JsonToken.Null:
            case JsonToken.Undefined:
              if (objectType == typeof(DBNull))
                return DBNull.Value;
              else
                return null;
              break;
            case JsonToken.Comment:
              // ignore
              break;
            default:
              throw new JsonSerializationException("Unexpected token while deserializing object: " + reader.TokenType);
          }
        } while (reader.Read());

        throw new JsonSerializationException("Unexpected end when deserializing object.");
      }
    }

    private void CheckedRead(JsonReader reader)
    {
      if (!reader.Read())
        throw new JsonSerializationException("Unexpected end when deserializing object.");
    }

    private object CreateList(JsonReader reader, Type objectType, object existingValue, string reference)
    {
      object value;
      if (objectType != null)
      {
        if (existingValue == null)
          value = CreateAndPopulateList(reader, objectType, reference);
        else
          value = PopulateList(CollectionUtils.CreateCollectionWrapper(existingValue), ReflectionUtils.GetCollectionItemType(objectType), reader, reference);
      }
      else
      {
        value = CreateJToken(reader);
      }
      return value;
    }

    private object EnsureType(object value, Type targetType)
    {
      // do something about null value when the targetType is a valuetype?
      if (value == null)
        return null;

      if (targetType == null)
        return value;

      Type valueType = value.GetType();

      // type of value and type of target don't match
      // attempt to convert value's type to target's type
      if (valueType != targetType)
      {
        return ConvertUtils.ConvertOrCast(value, CultureInfo.InvariantCulture, targetType);
      }
      else
      {
        return value;
      }
    }

    private void SetObjectMember(JsonReader reader, object target, Type targetType, string memberName)
    {
      JsonMemberMappingCollection memberMappings = _serializer.GetMemberMappings(targetType);

      JsonMemberMapping memberMapping;
      // attempt exact case match first
      // then try match ignoring case
      if (memberMappings.TryGetClosestMatchMapping(memberName, out memberMapping))
      {
        SetMappingValue(memberMapping, reader, target);
      }
      else
      {
        if (_serializer.MissingMemberHandling == MissingMemberHandling.Error)
          throw new JsonSerializationException("Could not find member '{0}' on object of type '{1}'".FormatWith(CultureInfo.InvariantCulture, memberName, targetType.Name));

        reader.Skip();
      }
    }

    private void SetMappingValue(JsonMemberMapping memberMapping, JsonReader reader, object target)
    {
      if (memberMapping.Ignored)
      {
        reader.Skip();
        return;
      }

      // get the member's underlying type
      Type memberType = ReflectionUtils.GetMemberUnderlyingType(memberMapping.Member);

      object currentValue = null;
      bool useExistingValue = false;

      if ((_serializer.ObjectCreationHandling == ObjectCreationHandling.Auto || _serializer.ObjectCreationHandling == ObjectCreationHandling.Reuse)
          && (reader.TokenType == JsonToken.StartArray || reader.TokenType == JsonToken.StartObject))
      {
        currentValue = ReflectionUtils.GetMemberValue(memberMapping.Member, target);

        useExistingValue = (currentValue != null && !memberType.IsArray && !ReflectionUtils.InheritsGenericDefinition(memberType, typeof(ReadOnlyCollection<>)));
      }

      if (!memberMapping.Writable && !useExistingValue)
      {
        reader.Skip();
        return;
      }

      object value = CreateValue(reader, memberType, (useExistingValue) ? currentValue : null, JsonTypeReflector.GetConverter(memberMapping.Member, memberType));

      if (!useExistingValue && ShouldSetMappingValue(memberMapping, value))
        ReflectionUtils.SetMemberValue(memberMapping.Member, target, value);
    }

    private bool ShouldSetMappingValue(JsonMemberMapping memberMapping, object value)
    {
      if (memberMapping.NullValueHandling.GetValueOrDefault(_serializer.NullValueHandling) == NullValueHandling.Ignore && value == null)
        return false;

      if (memberMapping.DefaultValueHandling.GetValueOrDefault(_serializer.DefaultValueHandling) == DefaultValueHandling.Ignore && Equals(value, memberMapping.DefaultValue))
        return false;

      if (!memberMapping.Writable)
        return false;

      return true;
    }

    private object CreateAndPopulateDictionary(JsonReader reader, Type objectType, string id)
    {
      if (IsTypeGenericDictionaryInterface(objectType))
      {
        Type keyType;
        Type valueType;
        ReflectionUtils.GetDictionaryKeyValueTypes(objectType, out keyType, out valueType);
        objectType = ReflectionUtils.MakeGenericType(typeof(Dictionary<,>), keyType, valueType);
      }

      IWrappedDictionary dictionary = CollectionUtils.CreateDictionaryWrapper(Activator.CreateInstance(objectType));

      PopulateDictionary(dictionary, reader, id);

      return dictionary.UnderlyingDictionary;
    }

    private IDictionary PopulateDictionary(IWrappedDictionary dictionary, JsonReader reader, string id)
    {
      Type dictionaryType = dictionary.UnderlyingDictionary.GetType();
      Type dictionaryKeyType = ReflectionUtils.GetDictionaryKeyType(dictionaryType);
      Type dictionaryValueType = ReflectionUtils.GetDictionaryValueType(dictionaryType);

      if (id != null)
        _serializer.ReferenceResolver.AddReference(id, dictionary.UnderlyingDictionary);

      do
      {
        switch (reader.TokenType)
        {
          case JsonToken.PropertyName:
            object keyValue = EnsureType(reader.Value, dictionaryKeyType);
            CheckedRead(reader);

            dictionary.Add(keyValue, CreateValue(reader, dictionaryValueType, null, null));
            break;
          case JsonToken.EndObject:
            return dictionary;
          default:
            throw new JsonSerializationException("Unexpected token when deserializing object: " + reader.TokenType);
        }
      } while (reader.Read());

      throw new JsonSerializationException("Unexpected end when deserializing object.");
    }

    private object CreateAndPopulateList(JsonReader reader, Type objectType, string reference)
    {
      if (IsTypeGenericCollectionInterface(objectType))
      {
        Type itemType = ReflectionUtils.GetCollectionItemType(objectType);
        objectType = ReflectionUtils.MakeGenericType(typeof(List<>), itemType);
      }

      return CollectionUtils.CreateAndPopulateList(objectType, (l, isTemporaryListReference) =>
        {
          if (reference != null && isTemporaryListReference)
            throw new JsonSerializationException("Cannot preserve reference to array or readonly list: {0}".FormatWith(CultureInfo.InvariantCulture, objectType));

          PopulateList(l, ReflectionUtils.GetCollectionItemType(objectType), reader, reference);
        });
    }

    private IList PopulateList(IList list, Type listItemType, JsonReader reader, string reference)
    {
      if (reference != null)
        _serializer.ReferenceResolver.AddReference(reference, list);

      while (reader.Read())
      {
        switch (reader.TokenType)
        {
          case JsonToken.EndArray:
            return list;
          case JsonToken.Comment:
            break;
          default:
            object value = CreateValue(reader, listItemType, null, null);

            list.Add(value);
            break;
        }
      }

      throw new JsonSerializationException("Unexpected end when deserializing array.");
    }

    private bool IsTypeGenericDictionaryInterface(Type type)
    {
      if (!type.IsGenericType)
        return false;

      Type genericDefinition = type.GetGenericTypeDefinition();

      return (genericDefinition == typeof(IDictionary<,>));
    }

    private bool IsTypeGenericCollectionInterface(Type type)
    {
      if (!type.IsGenericType)
        return false;

      Type genericDefinition = type.GetGenericTypeDefinition();

      return (genericDefinition == typeof(IList<>)
              || genericDefinition == typeof(ICollection<>)
              || genericDefinition == typeof(IEnumerable<>));
    }

    private object CreateAndPopulateObject(JsonReader reader, Type objectType, string id)
    {
      object newObject;

      if (objectType.IsInterface || objectType.IsAbstract)
        throw new JsonSerializationException("Could not create an instance of type {0}. Type is an interface or abstract class and cannot be instantated.".FormatWith(CultureInfo.InvariantCulture, objectType));

      if (ReflectionUtils.HasDefaultConstructor(objectType))
      {
        newObject = Activator.CreateInstance(objectType);

        PopulateObject(newObject, reader, objectType, id);
        return newObject;
      }

      return CreateObjectFromNonDefaultConstructor(objectType, reader);
    }

    private object CreateObjectFromNonDefaultConstructor(Type objectType, JsonReader reader)
    {
      // object should have a single constructor
      ConstructorInfo c = objectType.GetConstructors(BindingFlags.Public | BindingFlags.Instance).SingleOrDefault();

      if (c == null)
        throw new JsonSerializationException("Could not find a public constructor for type {0}.".FormatWith(CultureInfo.InvariantCulture, objectType));

      // create a dictionary to put retrieved values into
      JsonMemberMappingCollection memberMappings = _serializer.GetMemberMappings(objectType);
      IDictionary<JsonMemberMapping, object> mappingValues = memberMappings.ToDictionary(kv => kv, kv => (object)null);

      bool exit = false;
      do
      {
        switch (reader.TokenType)
        {
          case JsonToken.PropertyName:
            string memberName = reader.Value.ToString();
            if (!reader.Read())
              throw new JsonSerializationException("Unexpected end when setting {0}'s value.".FormatWith(CultureInfo.InvariantCulture, memberName));

            JsonMemberMapping memberMapping;
            // attempt exact case match first
            // then try match ignoring case
            if (memberMappings.TryGetClosestMatchMapping(memberName, out memberMapping))
            {
              if (!memberMapping.Ignored)
              {
                Type memberType = ReflectionUtils.GetMemberUnderlyingType(memberMapping.Member);
                mappingValues[memberMapping] = CreateValue(reader, memberType, null, memberMapping.MemberConverter);
              }
            }
            else
            {
              if (_serializer.MissingMemberHandling == MissingMemberHandling.Error)
                throw new JsonSerializationException("Could not find member '{0}' on object of type '{1}'".FormatWith(CultureInfo.InvariantCulture, memberName, objectType.Name));

              reader.Skip();
            }
            break;
          case JsonToken.EndObject:
            exit = true;
            break;
          default:
            throw new JsonSerializationException("Unexpected token when deserializing object: " + reader.TokenType);
        }
      } while (!exit && reader.Read());

      IDictionary<ParameterInfo, object> constructorParameters = c.GetParameters().ToDictionary(p => p, p => (object)null);
      IDictionary<JsonMemberMapping, object> remainingMappingValues = new Dictionary<JsonMemberMapping, object>();

      foreach (KeyValuePair<JsonMemberMapping, object> mappingValue in mappingValues)
      {
        ParameterInfo matchingConstructorParameter = constructorParameters.ForgivingCaseSensitiveFind(kv => kv.Key.Name, mappingValue.Key.PropertyName).Key;
        if (matchingConstructorParameter != null)
          constructorParameters[matchingConstructorParameter] = mappingValue.Value;
        else
          remainingMappingValues.Add(mappingValue);
      }

      object createdObject = ReflectionUtils.CreateInstance(objectType, constructorParameters.Values.ToArray());

      // go through unused values and set the newly created object's properties
      foreach (KeyValuePair<JsonMemberMapping, object> remainingMappingValue in remainingMappingValues)
      {
        if (ShouldSetMappingValue(remainingMappingValue.Key, remainingMappingValue.Value))
          ReflectionUtils.SetMemberValue(remainingMappingValue.Key.Member, createdObject, remainingMappingValue.Value);
      }

      return createdObject;
    }

    private object PopulateObject(object newObject, JsonReader reader, Type objectType, string id)
    {
      JsonMemberMappingCollection memberMappings = _serializer.GetMemberMappings(objectType);
      Dictionary<string, bool> requiredMappings =
        memberMappings.Where(m => m.Required).ToDictionary(m => m.PropertyName, m => false);

      if (id != null)
        _serializer.ReferenceResolver.AddReference(id, newObject);

      do
      {
        switch (reader.TokenType)
        {
          case JsonToken.PropertyName:
            string memberName = reader.Value.ToString();

            if (!reader.Read())
              throw new JsonSerializationException("Unexpected end when setting {0}'s value.".FormatWith(CultureInfo.InvariantCulture, memberName));

            if (reader.TokenType != JsonToken.Null)
              SetRequiredMapping(memberName, requiredMappings);
            
            SetObjectMember(reader, newObject, objectType, memberName);
            break;
          case JsonToken.EndObject:
            foreach (KeyValuePair<string, bool> requiredMapping in requiredMappings)
            {
              if (!requiredMapping.Value)
                throw new JsonSerializationException("Required property '{0}' not found in JSON.".FormatWith(CultureInfo.InvariantCulture, requiredMapping.Key));
            }
            return newObject;
          case JsonToken.Comment:
            // ignore
            break;
          default:
            throw new JsonSerializationException("Unexpected token when deserializing object: " + reader.TokenType);
        }
      } while (reader.Read());

      throw new JsonSerializationException("Unexpected end when deserializing object.");
    }

    private void SetRequiredMapping(string memberName, Dictionary<string, bool> requiredMappings)
    {
      // first attempt to find exact case match
      // then attempt case insensitive match
      if (requiredMappings.ContainsKey(memberName))
      {
        requiredMappings[memberName] = true;
      }
      else
      {
        foreach (KeyValuePair<string, bool> requiredMapping in requiredMappings)
        {
          if (string.Compare(requiredMapping.Key, requiredMapping.Key, StringComparison.OrdinalIgnoreCase) == 0)
          {
            requiredMappings[requiredMapping.Key] = true;
            break;
          }
        }
      }
    }
  }
}