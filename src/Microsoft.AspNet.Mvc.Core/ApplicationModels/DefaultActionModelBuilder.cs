﻿// Copyright (c) Microsoft Open Technologies, Inc. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.AspNet.Mvc.Description;
using Microsoft.AspNet.Mvc.ModelBinding;
using Microsoft.AspNet.Mvc.Routing;

namespace Microsoft.AspNet.Mvc.ApplicationModels
{
    /// <summary>
    /// A default implementation of <see cref="IActionModelBuilder"/>.
    /// </summary>
    public class DefaultActionModelBuilder : IActionModelBuilder
    {
        /// <inheritdoc />
        public IEnumerable<ActionModel> BuildActionModels([NotNull] MethodInfo methodInfo)
        {
            if (!IsAction(methodInfo))
            {
                return Enumerable.Empty<ActionModel>();
            }

            // CoreCLR returns IEnumerable<Attribute> from GetCustomAttributes - the OfType<object>
            // is needed to so that the result of ToArray() is object
            var attributes = methodInfo.GetCustomAttributes(inherit: true).OfType<object>().ToArray();

            // Route attributes create multiple actions, we want to split the set of
            // attributes based on these so each action only has the attributes that affect it.
            //
            // The set of route attributes are split into those that 'define' a route versus those that are
            // 'silent'.
            //
            // We need to define from action for each attribute that 'defines' a route, and a single action
            // for all of the ones that don't (if any exist).
            // 
            // Ex:
            // [HttpGet]
            // [AcceptVerbs("POST", "PUT")]
            // [Route("Api/Things")]
            // public void DoThing() 
            //
            // This will generate 2 actions:
            // 1. [Route("Api/Things")]
            // 2. [HttpGet], [AcceptVerbs("POST", "PUT")]
            //
            // Note that having a route attribute that doesn't define a route template _might_ be an error. We
            // don't have enough context to really know at this point so we just pass it on.
            var splitAttributes = new List<object>();

            var hasSilentRouteAttribute = false;
            foreach (var attribute in attributes)
            {
                var routeTemplateProvider = attribute as IRouteTemplateProvider;
                if (routeTemplateProvider != null)
                {
                    if (IsSilentRouteAttribute(routeTemplateProvider))
                    {
                        hasSilentRouteAttribute = true;
                    }
                    else
                    {
                        splitAttributes.Add(attribute);
                    }
                }
            }

            var actionModels = new List<ActionModel>();
            if (splitAttributes.Count == 0 && !hasSilentRouteAttribute)
            {
                actionModels.Add(CreateActionModel(methodInfo, attributes));
            }
            else
            {
                foreach (var splitAttribute in splitAttributes)
                {
                    var filteredAttributes = new List<object>();
                    foreach (var attribute in attributes)
                    {
                        if (attribute == splitAttribute)
                        {
                            filteredAttributes.Add(attribute);
                        }
                        else if (attribute is IRouteTemplateProvider)
                        {
                            // Exclude other route template providers
                        }
                        else
                        {
                            filteredAttributes.Add(attribute);
                        }
                    }

                    actionModels.Add(CreateActionModel(methodInfo, filteredAttributes));
                }

                if (hasSilentRouteAttribute)
                {
                    var filteredAttributes = new List<object>();
                    foreach (var attribute in attributes)
                    {
                        if (!splitAttributes.Contains(attribute))
                        {
                            filteredAttributes.Add(attribute);
                        }
                    }

                    actionModels.Add(CreateActionModel(methodInfo, filteredAttributes));
                }
            }

            foreach (var actionModel in actionModels)
            {
                foreach (var parameterInfo in actionModel.ActionMethod.GetParameters())
                {
                    var parameterModel = CreateParameterModel(parameterInfo);
                    if (parameterModel != null)
                    {
                        parameterModel.Action = actionModel;
                        actionModel.Parameters.Add(parameterModel);
                    }
                }
            }

            return actionModels;
        }

        /// <summary>
        /// Returns <c>true</c> if the <paramref name="methodInfo"/> is an action. Otherwise <c>false</c>.
        /// </summary>
        /// <param name="methodInfo">The <see cref="MethodInfo"/>.</param>
        /// <returns><c>true</c> if the <paramref name="methodInfo"/> is an action. Otherwise <c>false</c>.</returns>
        /// <remarks>
        /// Override this method to provide custom logic to determine which methods are considered actions.
        /// </remarks>
        protected virtual bool IsAction([NotNull] MethodInfo methodInfo)
        {
            return
                methodInfo.IsPublic &&
                !methodInfo.IsStatic &&
                !methodInfo.IsAbstract &&
                !methodInfo.IsConstructor &&
                !methodInfo.IsGenericMethod &&

                // The SpecialName bit is set to flag members that are treated in a special way by some compilers
                // (such as property accessors and operator overloading methods).
                !methodInfo.IsSpecialName &&
                !methodInfo.IsDefined(typeof(NonActionAttribute)) &&

                // Overriden methods from Object class, e.g. Equals(Object), GetHashCode(), etc., are not valid.
                methodInfo.GetBaseDefinition().DeclaringType != typeof(object);
        }

        /// <summary>
        /// Creates an <see cref="ActionModel"/> for the given <see cref="MethodInfo"/>.
        /// </summary>
        /// <param name="methodInfo">The <see cref="MethodInfo"/>.</param>
        /// <param name="attributes">The set of attributes to use as metadata.</param>
        /// <returns>An <see cref="ActionModel"/> for the given <see cref="MethodInfo"/>.</returns>
        /// <remarks>
        /// An action-method in code may expand into multiple <see cref="ActionModel"/> instances depending on how
        /// the action is routed. In the case of multiple routing attributes, this method will invoked be once for 
        /// each action that can be created.
        /// 
        /// If overriding this method, use the provided <paramref name="attributes"/> list to find metadata related to
        /// the action being created.
        /// </remarks>
        protected virtual ActionModel CreateActionModel(
            [NotNull] MethodInfo methodInfo, 
            [NotNull] IReadOnlyList<object> attributes)
        {
            var actionModel = new ActionModel(methodInfo)
            {
                IsActionNameMatchRequired = true,
            };

            actionModel.Attributes.AddRange(attributes);

            actionModel.ActionConstraints.AddRange(attributes.OfType<IActionConstraintMetadata>());
            actionModel.Filters.AddRange(attributes.OfType<IFilter>());

            var actionName = attributes.OfType<ActionNameAttribute>().FirstOrDefault();
            if (actionName?.Name != null)
            {
                actionModel.ActionName = actionName.Name;
            }
            else
            {
                actionModel.ActionName = methodInfo.Name;
            }

            var apiVisibility = attributes.OfType<IApiDescriptionVisibilityProvider>().FirstOrDefault();
            if (apiVisibility != null)
            {
                actionModel.ApiExplorer.IsVisible = !apiVisibility.IgnoreApi;
            }

            var apiGroupName = attributes.OfType<IApiDescriptionGroupNameProvider>().FirstOrDefault();
            if (apiGroupName != null)
            {
                actionModel.ApiExplorer.GroupName = apiGroupName.GroupName;
            }

            var httpMethods = attributes.OfType<IActionHttpMethodProvider>();
            actionModel.HttpMethods.AddRange(
                httpMethods
                    .Where(a => a.HttpMethods != null)
                    .SelectMany(a => a.HttpMethods)
                    .Distinct());

            var routeTemplateProvider = attributes.OfType<IRouteTemplateProvider>().FirstOrDefault();
            if (routeTemplateProvider != null && !IsSilentRouteAttribute(routeTemplateProvider))
            {
                actionModel.AttributeRouteModel = new AttributeRouteModel(routeTemplateProvider);
            }

            return actionModel;
        }

        /// <summary>
        /// Creates a <see cref="ParameterModel"/> for the given <see cref="ParameterInfo"/>.
        /// </summary>
        /// <param name="parameterInfo">The <see cref="ParameterInfo"/>.</param>
        /// <returns>A <see cref="ParameterModel"/> for the given <see cref="ParameterInfo"/>.</returns>
        protected virtual ParameterModel CreateParameterModel([NotNull] ParameterInfo parameterInfo)
        {
            var parameterModel = new ParameterModel(parameterInfo);

            // CoreCLR returns IEnumerable<Attribute> from GetCustomAttributes - the OfType<object>
            // is needed to so that the result of ToArray() is object
            var attributes = parameterInfo.GetCustomAttributes(inherit: true).OfType<object>().ToArray();
            parameterModel.Attributes.AddRange(attributes);

            parameterModel.BinderMetadata = attributes.OfType<IBinderMetadata>().FirstOrDefault();

            parameterModel.ParameterName = parameterInfo.Name;
            parameterModel.IsOptional = parameterInfo.HasDefaultValue;

            return parameterModel;
        }

        private bool IsSilentRouteAttribute(IRouteTemplateProvider routeTemplateProvider)
        {
            return 
                routeTemplateProvider.Template == null && 
                routeTemplateProvider.Order == null &&
                routeTemplateProvider.Name == null;
        }
    }
}