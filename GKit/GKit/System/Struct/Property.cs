﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GKit {
	[Serializable]
	public class Property<T> {
		public T Value {
			get {
				return value;
			}
			set {
				SetValue(value);
			}
		}
		public delegate void ValueChangedDelegate(T before, T newValue);
		public delegate void ValueChangingDelegate(T before, ref T newValue);
		public event Action OnValueChangedSimple;
		public event ValueChangedDelegate OnValueChanged;
		public event ValueChangingDelegate OnValueChanging;
#if UNITY
		[UnityEngine.SerializeField]
#endif
		private T value;

		public Property() {
		}
		public Property(T value) {
			this.value = value;
		}
		public void SetValue(T value) {
			T before = this.value;
			OnValueChanging?.Invoke(before, ref value);
			SetValueNoEvent(value);
			OnValueChanged?.Invoke(before, value);
			OnValueChangedSimple?.Invoke();
		}
		public void SetValueNoEvent(T value) {
			this.value = value;
		}

		public void RunEvent() {
			OnValueChanged?.Invoke(value, value);
			OnValueChangedSimple?.Invoke();
		}
		public void ClearEvent() {
			OnValueChanging = null;
			OnValueChanged = null;
		}
	}
}
