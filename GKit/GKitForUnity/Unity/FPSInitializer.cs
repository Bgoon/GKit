﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace GKitForUnity {
	public class FpsInitializer : MonoBehaviour {
		public int targetFps = 60;

		private void Start() {
			Application.targetFrameRate = targetFps;
		}
	}
}
