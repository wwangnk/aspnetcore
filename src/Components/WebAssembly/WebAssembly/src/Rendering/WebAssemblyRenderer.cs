// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.RenderTree;
using Microsoft.AspNetCore.Components.WebAssembly.Services;
using Microsoft.Extensions.Logging;
using Microsoft.JSInterop.WebAssembly;

namespace Microsoft.AspNetCore.Components.WebAssembly.Rendering
{
    /// <summary>
    /// Provides mechanisms for rendering <see cref="IComponent"/> instances in a
    /// web browser, dispatching events to them, and refreshing the UI as required.
    /// </summary>
    internal class WebAssemblyRenderer : Renderer
    {
        private readonly ILogger _logger;
        private readonly int _webAssemblyRendererId;

        /// <summary>
        /// Constructs an instance of <see cref="WebAssemblyRenderer"/>.
        /// </summary>
        /// <param name="serviceProvider">The <see cref="IServiceProvider"/> to use when initializing components.</param>
        /// <param name="loggerFactory">The <see cref="ILoggerFactory"/>.</param>
        public WebAssemblyRenderer(IServiceProvider serviceProvider, ILoggerFactory loggerFactory)
            : base(serviceProvider, loggerFactory)
        {
            // The WebAssembly renderer registers and unregisters itself with the static registry
            _webAssemblyRendererId = RendererRegistry.Add(this);
            _logger = loggerFactory.CreateLogger<WebAssemblyRenderer>();

            ElementReferenceContext = DefaultWebAssemblyJSRuntime.Instance.ElementReferenceContext;
        }

        public override Dispatcher Dispatcher => NullDispatcher.Instance;

        /// <summary>
        /// Attaches a new root component to the renderer,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <typeparam name="TComponent">The type of the component.</typeparam>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous rendering of the added component.</returns>
        /// <remarks>
        /// Callers of this method may choose to ignore the returned <see cref="Task"/> if they do not
        /// want to await the rendering of the added component.
        /// </remarks>
        public Task AddComponentAsync<TComponent>(string domElementSelector) where TComponent : IComponent
            => AddComponentAsync(typeof(TComponent), domElementSelector);

        /// <summary>
        /// Associates the <see cref="IComponent"/> with the <see cref="WebAssemblyRenderer"/>,
        /// causing it to be displayed in the specified DOM element.
        /// </summary>
        /// <param name="componentType">The type of the component.</param>
        /// <param name="domElementSelector">A CSS selector that uniquely identifies a DOM element.</param>
        /// <returns>A <see cref="Task"/> that represents the asynchronous rendering of the added component.</returns>
        /// <remarks>
        /// Callers of this method may choose to ignore the returned <see cref="Task"/> if they do not
        /// want to await the rendering of the added component.
        /// </remarks>
        public Task AddComponentAsync(Type componentType, string domElementSelector)
        {
            var component = InstantiateComponent(componentType);
            var componentId = AssignRootComponentId(component);

            // The only reason we're calling this synchronously is so that, if it throws,
            // we get the exception back *before* attempting the first UpdateDisplayAsync
            // (otherwise the logged exception will come from UpdateDisplayAsync instead of here)
            // When implementing support for out-of-process runtimes, we'll need to call this
            // asynchronously and ensure we surface any exceptions correctly.

            DefaultWebAssemblyJSRuntime.Instance.Invoke<object>(
                "Blazor._internal.attachRootComponentToElement",
                domElementSelector,
                componentId,
                _webAssemblyRendererId);

            return RenderRootComponentAsync(componentId);
        }

        /// <inheritdoc />
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            RendererRegistry.TryRemove(_webAssemblyRendererId);
        }

        /// <inheritdoc />
        protected override Task UpdateDisplayAsync(in RenderBatch batch)
        {
            DefaultWebAssemblyJSRuntime.Instance.InvokeUnmarshalled<int, RenderBatch, object>(
                "Blazor._internal.renderBatch",
                _webAssemblyRendererId,
                batch);

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        protected override void HandleException(Exception exception)
        {
            if (exception is AggregateException aggregateException)
            {
                foreach (var innerException in aggregateException.Flatten().InnerExceptions)
                {
                    Log.UnhandledExceptionRenderingComponent(_logger, innerException);
                }
            }
            else
            {
                Log.UnhandledExceptionRenderingComponent(_logger, exception);
            }
        }


        private static class Log
        {
            private static readonly Action<ILogger, string, Exception> _unhandledExceptionRenderingComponent;

            private static class EventIds
            {
                public static readonly EventId UnhandledExceptionRenderingComponent = new EventId(100, "ExceptionRenderingComponent");
            }

            static Log()
            {
                _unhandledExceptionRenderingComponent = LoggerMessage.Define<string>(
                    LogLevel.Critical,
                    EventIds.UnhandledExceptionRenderingComponent,
                    "Unhandled exception rendering component: {Message}");
            }

            public static void UnhandledExceptionRenderingComponent(ILogger logger, Exception exception)
            {
                _unhandledExceptionRenderingComponent(
                    logger,
                    exception.Message,
                    exception);
            }
        }
    }
}
