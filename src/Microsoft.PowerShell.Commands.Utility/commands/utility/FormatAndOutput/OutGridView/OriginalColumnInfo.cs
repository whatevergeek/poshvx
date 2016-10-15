//
//    Copyright (C) Microsoft.  All rights reserved.
//

namespace Microsoft.PowerShell.Commands
{
    using System;
    using System.Collections;
    using System.Management.Automation;
    using Microsoft.PowerShell.Commands.Internal.Format;

    internal class OriginalColumnInfo : ColumnInfo
    {
        private string _liveObjectPropertyName;
        private OutGridViewCommand _parentCmdlet;

        internal OriginalColumnInfo(string staleObjectPropertyName, string displayName, string liveObjectPropertyName, OutGridViewCommand parentCmdlet)
            : base(staleObjectPropertyName, displayName)
        {
            _liveObjectPropertyName = liveObjectPropertyName;
            _parentCmdlet = parentCmdlet;
        }

        internal override Object GetValue(PSObject liveObject)
        {
            try
            {
                PSPropertyInfo propertyInfo = liveObject.Properties[_liveObjectPropertyName];
                if (propertyInfo == null)
                {
                    return null;
                }

                // The live object has the liveObjectPropertyName property.
                Object liveObjectValue = propertyInfo.Value;
                ICollection collectionValue = liveObjectValue as ICollection;
                if (null != collectionValue)
                {
                    liveObjectValue = _parentCmdlet.ConvertToString(PSObjectHelper.AsPSObject(propertyInfo.Value));
                }
                else
                {
                    PSObject psObjectValue = liveObjectValue as PSObject;
                    if (psObjectValue != null)
                    {
                        // Since PSObject implements IComparable there is a need to verify if its BaseObject actually implements IComparable.
                        if (psObjectValue.BaseObject is IComparable)
                        {
                            liveObjectValue = psObjectValue;
                        }
                        else
                        {
                            // Use the String type as default.
                            liveObjectValue = _parentCmdlet.ConvertToString(psObjectValue);
                        }
                    }
                }

                return ColumnInfo.LimitString(liveObjectValue);
            }
            catch (GetValueException)
            {
                // ignore
            }
            catch (ExtendedTypeSystemException)
            {
                // ignore
            }
            return null;
        }
    }
}
