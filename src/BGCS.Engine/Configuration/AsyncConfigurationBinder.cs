using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using BGCS.Core.Common;
using BGCS.Core.Interop;
using BGCS.Core.Native;

namespace BGCS.Configuration;

internal static class AsyncConfigurationBinder
{
    internal static ImmutableArray<InteropFunction> Apply(
        InteropModule module,
        ImmutableArray<InteropFunction> functions,
        ImmutableArray<AsyncOperationConfiguration> rules,
        string origin,
        Func<string, InteropFunction> select
    ) {
        foreach (AsyncOperationConfiguration rule in rules)
        {
            StableId selected = select(rule.function).id;
            int index = Array.FindIndex(functions.ToArray(), function => function.id == selected);
            InteropFunction function = functions[index];
            InteropParameter completion = Parameter(function, rule.completionCallback);
            CallbackContract callback = completion.callback ?? throw new ArgumentException("An asynchronous completion parameter requires its explicit retained callback registration protocol.");
            InteropType callable = Resolve(callback.callableType);
            if (rule.resultArgumentIndex >= callable.parameterTypes.Length || rule.statusArgumentIndex >= callable.parameterTypes.Length)
                throw new ArgumentException("An async completion result or status index lies outside the native prototype.");
            Evidence evidence = new(EvidenceLevel.UserAsserted, origin + ":semantics:async:" + rule.function);
            InteropType status = Resolve(callable.parameterTypes[rule.statusArgumentIndex]);
            StableId? cancel = null, handle = null;
            if (rule.cancelFunction is not null)
            {
                InteropFunction cancellation = select(rule.cancelFunction);
                cancel = cancellation.id;
                handle = Parameter(cancellation, rule.cancelHandleParameter ?? throw new ArgumentException("An async cancellation request requires its exact handle input.")).id;
            }
            AsyncContract contract = new(completion.id, rule.completionQuiescence, evidence)
            {
                resultArgumentIndex = rule.resultArgumentIndex,
                statusArgumentIndex = rule.statusArgumentIndex,
                successStatus = CallbackConfigurationBinder.ScalarFallback(status, rule.successStatus, evidence),
                cancellationStatus = rule.cancellationStatus is null ? null : CallbackConfigurationBinder.ScalarFallback(status, rule.cancellationStatus, evidence),
                cancelFunction = cancel,
                cancellationHandleParameter = handle,
                cancellationQuiescence = rule.cancellationQuiescence,
                cancellationIsNonBlocking = rule.cancellationIsNonBlocking,
                retainedParameters = rule.retainedParameters.Select(name => Parameter(function, name).id).ToImmutableArray()
            };
            functions = functions.SetItem(index, function with { asynchronous = contract });
        }
        return functions;

        static InteropParameter Parameter(
            InteropFunction function,
            string selector
        ) {
            InteropParameter[] matches = function.parameters.Where(parameter => parameter.name == selector || parameter.sourceName == selector).ToArray();
            return matches.Length == 1 ? matches[0] : throw new ArgumentException("An async rule must identify exactly one original native parameter: " + selector);
        }
        InteropType Resolve(StableId id)
        {
            HashSet<StableId> seen = [];
            InteropType type = module.GetType(id);
            while (type.category is NativeTypeCategory.Alias or NativeTypeCategory.Qualified or NativeTypeCategory.Enumeration)
            {
                if (!seen.Add(type.id) || type.elementType is not StableId element) throw new ArgumentException("An async rule contains a missing or cyclic native type.");
                type = module.GetType(element);
            }
            return type;
        }
    }
}
