﻿//-----------------------------------------------------------------------
// <copyright file="WeakEventListener.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;
    using System.Windows;

    /// <summary>
    /// A common weak event listener which can be used for different kinds of events.
    /// </summary>
    /// <typeparam name="TEventArgs">The EventArgs type for the event.</typeparam>
    class WeakEventListener<TEventArgs> : IWeakEventListener where TEventArgs : EventArgs
    {
        EventHandler<TEventArgs> realHander;

        /// <summary>
        /// Constructs an instance of WeakEventListener.
        /// </summary>
        /// <param name="handler">The handler for the event.</param>
        public WeakEventListener(EventHandler<TEventArgs> handler)
        {
            if (handler == null)
            {
                throw new ArgumentNullException("handler");
            }

            this.realHander = handler;
        }

        /// <summary>
        /// Receives events from the centralized event manager.
        /// </summary>
        /// <param name="managerType">The type of the WeakEventManager calling this method.</param>
        /// <param name="sender">Object that originated the event.</param>
        /// <param name="e">Event data.</param>
        /// <returns>
        /// true if the listener handled the event. It is considered an error by the WeakEventManager handling in WPF to register a listener for an event that the listener does not handle. Regardless, the method should return false if it receives an event that it does not recognize or handle.
        /// </returns>
        public bool ReceiveWeakEvent(Type managerType, Object sender, EventArgs e)
        {
            TEventArgs realArgs = (TEventArgs)e;

            this.realHander(sender, realArgs);

            return true;
        }
    }
}
