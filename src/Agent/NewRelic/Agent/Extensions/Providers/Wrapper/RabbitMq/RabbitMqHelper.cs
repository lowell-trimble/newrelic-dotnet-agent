// Copyright 2020 New Relic, Inc. All rights reserved.
// SPDX-License-Identifier: Apache-2.0

using System;
using System.Collections.Generic;
using System.Text;
using NewRelic.Agent.Api;
using NewRelic.Agent.Extensions.Providers.Wrapper;
using NewRelic.Reflection;
using NewRelic.SystemExtensions;

namespace NewRelic.Providers.Wrapper.RabbitMq
{
    public class RabbitMqHelper
    {
        private const string TempQueuePrefix = "amq.";
        private const string BasicPropertiesType = "RabbitMQ.Client.Framing.BasicProperties";
        public const string VendorName = "RabbitMQ";
        public const string AssemblyName = "RabbitMQ.Client";
        public const string TypeName = "RabbitMQ.Client.Framing.Impl.Model";

        private static Func<object, object> _getHeadersFunc;
        public static IDictionary<string, object> GetHeaders(object properties)
        {
            var func = _getHeadersFunc ?? (_getHeadersFunc = VisibilityBypasser.Instance.GeneratePropertyAccessor<object>(properties.GetType(), "Headers"));
            return func(properties) as IDictionary<string, object>;
        }

        public static void SetHeaders(object properties, IDictionary<string, object> headers)
        {
            // Unlike the GetHeaders function, we can't cache this action.  It is only valid for the specific Properties object instance provided.
            var action = VisibilityBypasser.Instance.GeneratePropertySetter<IDictionary<string, object>>(properties, "Headers");

            action(headers);
        }

        public static MessageBrokerDestinationType GetBrokerDestinationType(string queueNameOrRoutingKey)
        {
            if (queueNameOrRoutingKey.StartsWith(TempQueuePrefix))
                return MessageBrokerDestinationType.TempQueue;

            return queueNameOrRoutingKey.Contains(".") ? MessageBrokerDestinationType.Topic : MessageBrokerDestinationType.Queue;
        }

        public static string ResolveDestinationName(MessageBrokerDestinationType destinationType,
            string queueNameOrRoutingKey)
        {
            return (destinationType == MessageBrokerDestinationType.TempQueue ||
                    destinationType == MessageBrokerDestinationType.TempTopic)
                ? null
                : queueNameOrRoutingKey;
        }

        public static ISegment CreateSegmentForPublishWrappers(InstrumentedMethodCall instrumentedMethodCall, ITransaction transaction, int basicPropertiesIndex)
        {
            // ATTENTION: We have validated that the use of dynamic here is appropriate based on the visibility of the data we're working with.
            // If we implement newer versions of the API or new methods we'll need to re-evaluate.
            // never null. Headers property can be null.
            var basicProperties = instrumentedMethodCall.MethodCall.MethodArguments.ExtractAs<dynamic>(basicPropertiesIndex);

            var routingKey = instrumentedMethodCall.MethodCall.MethodArguments.ExtractNotNullAs<string>(1);
            var destType = GetBrokerDestinationType(routingKey);
            var destName = ResolveDestinationName(destType, routingKey);

            var segment = transaction.StartMessageBrokerSegment(instrumentedMethodCall.MethodCall, destType, MessageBrokerAction.Produce, VendorName, destName);

            //If the RabbitMQ version doesn't provide the BasicProperties parameter we just bail.
            if (basicProperties.GetType().FullName != BasicPropertiesType)
            {
                return segment;
            }

            var setHeaders = new Action<dynamic, string, string>((carrier, key, value) =>
            {
                var headers = carrier.Headers as IDictionary<string, object>;

                if (headers == null)
                {
                    headers = new Dictionary<string, object>();
                    carrier.Headers = headers;
                }
                else if (headers is IReadOnlyDictionary<string, object>)
                {
                    headers = new Dictionary<string, object>(headers);
                    carrier.Headers = headers;
                }

                headers[key] = value;
            });

            transaction.InsertDistributedTraceHeaders(basicProperties, setHeaders);

            return segment;
        }

        public static ISegment CreateSegmentForPublishWrappers6Plus(InstrumentedMethodCall instrumentedMethodCall, ITransaction transaction, int basicPropertiesIndex)
        {
            var basicProperties = instrumentedMethodCall.MethodCall.MethodArguments.ExtractAs<object>(basicPropertiesIndex);

            var routingKey = instrumentedMethodCall.MethodCall.MethodArguments.ExtractNotNullAs<string>(1);
            var destType = GetBrokerDestinationType(routingKey);
            var destName = ResolveDestinationName(destType, routingKey);

            var segment = transaction.StartMessageBrokerSegment(instrumentedMethodCall.MethodCall, destType, MessageBrokerAction.Produce, VendorName, destName);

            //If the RabbitMQ version doesn't provide the BasicProperties parameter we just bail.
            if (basicProperties.GetType().FullName != BasicPropertiesType)
            {
                
                return segment;
            }

            var setHeaders = new Action<object, string, string>((carrier, key, value) =>
            {
                var headers = GetHeaders(carrier);

                if (headers == null)
                {
                    headers = new Dictionary<string, object>();
                    SetHeaders(carrier, headers);
                }
                else if (headers is IReadOnlyDictionary<string, object>)
                {
                    headers = new Dictionary<string, object>(headers);
                    SetHeaders(carrier, headers);
                }

                headers[key] = value;
            });

            transaction.InsertDistributedTraceHeaders(basicProperties, setHeaders);

            return segment;
        }
    }
}
