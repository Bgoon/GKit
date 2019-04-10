﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
#if UNITY
using UnityEngine;
#endif

namespace GKit {
	public class MotionValue<T> where T : struct {
		public T dstValue;
		public T currentValue;
		public T Delta => (dynamic)dstValue - (dynamic)currentValue;
		public event SingleDelegate<T> OnUpdated;

		public MotionValue() {

		}
		public void UpdateValue(float deltaFactor) {
			currentValue += ((dynamic)dstValue - (dynamic)currentValue) * deltaFactor;

			OnUpdated?.Invoke(currentValue);
		}
	}
}
