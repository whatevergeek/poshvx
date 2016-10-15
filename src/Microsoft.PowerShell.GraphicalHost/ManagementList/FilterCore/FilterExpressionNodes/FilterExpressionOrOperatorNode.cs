﻿//-----------------------------------------------------------------------
// <copyright file="FilterExpressionOrOperatorNode.cs" company="Microsoft">
//     Copyright (c) Microsoft Corporation.  All rights reserved.
// </copyright>
//-----------------------------------------------------------------------

namespace Microsoft.Management.UI.Internal
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;

    /// <summary>
    /// The FilterExpressionOrOperatorNode class is responsible for containing children
    /// FilterExpressionNodes which will be OR'ed together during evaluation.
    /// </summary>
    [SuppressMessage("Microsoft.MSInternal", "CA903:InternalNamespaceShouldNotContainPublicTypes")]
    public class FilterExpressionOrOperatorNode : FilterExpressionNode
    {
        #region Properties

        private List<FilterExpressionNode> children = new List<FilterExpressionNode>();

        /// <summary>
        /// Gets a collection FilterExpressionNode children used during evaluation.
        /// </summary>
        public ICollection<FilterExpressionNode> Children
        {
            get
            {
                return this.children;
            }
        }

        #endregion Properties

        #region Ctor

        /// <summary>
        /// Initializes a new instance of the FilterExpressionOrOperatorNode
        /// class.
        /// </summary>
        public FilterExpressionOrOperatorNode()
        {
            // empty
        }

        /// <summary>
        /// Initializes a new instance of the FilterExpressionOrOperatorNode
        /// class.
        /// </summary>
        /// <param name="children">
        /// A collection of children which will be added to the 
        /// FilterExpressionOrOperatorNode's Children collection.
        /// </param>
        public FilterExpressionOrOperatorNode(IEnumerable<FilterExpressionNode> children)
        {
            if (null == children)
            {
                throw new ArgumentNullException("children");
            }

            this.children.AddRange(children);
        }

        #endregion Ctor

        #region Public Methods

        /// <summary>
        /// Evaluates the children FilterExpressionNodes and returns
        /// the OR'ed result of their results.
        /// </summary>
        /// <param name="item">
        /// The item to evaluate against.
        /// </param>
        /// <returns>
        /// True if any FilterExpressionNode child evaluates to true,
        /// false otherwise.
        /// </returns>
        public override bool Evaluate(object item)
        {
            if (0 == this.Children.Count)
            {
                return false;
            }

            foreach (FilterExpressionNode node in this.Children)
            {
                if (node.Evaluate(item))
                {
                    return true;
                }
            }

            return false;
        }

        #endregion Public Methods
    }
}
