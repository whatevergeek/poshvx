//
// Copyright (c) 2008 Microsoft Corporation. All rights reserved.
// 

using System;
using System.Collections;
using System.Collections.Specialized;
using System.Collections.Generic;
using System.Diagnostics;
using System.ComponentModel;

namespace Microsoft.PowerShell.Commands.GetCounter
{
    public class CounterFileInfo
    {
        internal CounterFileInfo(DateTime oldestRecord,
            DateTime newestRecord,
            UInt32 sampleCount)
        {
            _oldestRecord = oldestRecord;
            _newestRecord = newestRecord;
            _sampleCount = sampleCount;
        }

        internal CounterFileInfo() { }

        public DateTime OldestRecord
        {
            get
            {
                return _oldestRecord;
            }
        }
        private DateTime _oldestRecord = DateTime.MinValue;

        public DateTime NewestRecord
        {
            get
            {
                return _newestRecord;
            }
        }
        private DateTime _newestRecord = DateTime.MaxValue;

        public UInt32 SampleCount
        {
            get
            {
                return _sampleCount;
            }
        }
        private UInt32 _sampleCount = 0;
    }
}

