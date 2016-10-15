﻿//-----------------------------------------------------------------------
// <copyright file="ListOrganizer.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System.Windows;
    using System.Windows.Controls;
    using System.Windows.Input;
    using System;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// Picker control that displays a list with basic editing functionality.
    /// </summary>
    [SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public partial class ListOrganizer : ContentControl
    {
        /// <summary>
        /// Creates a new instance of the ListOrganizer class.
        /// </summary>
        public ListOrganizer()
        {
            // empty
        }

        /// <summary>
        /// Prevents keyboard focus from leaving the dropdown.
        /// </summary>
        /// <param name="e">The event args.</param>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Up ||
                e.Key == Key.Down ||
                e.Key == Key.Left ||
                e.Key == Key.Right)
            {
                e.Handled = true;
            }
        }

        partial void OnSelectItemExecutedImplementation(ExecutedRoutedEventArgs e)
        {
            if (null == e.Parameter)
            {
                throw new ArgumentException("e.Parameter is null", "e");
            }

            this.RaiseEvent(new DataRoutedEventArgs<object>(e.Parameter, ItemSelectedEvent));
            this.picker.IsOpen = false;
        }

        partial void OnDeleteItemExecutedImplementation(ExecutedRoutedEventArgs e)
        {
            if (null == e.Parameter)
            {
                throw new ArgumentException("e.Parameter is null", "e");
            }

            this.RaiseEvent(new DataRoutedEventArgs<object>(e.Parameter, ItemDeletedEvent));
        }
    }
}
