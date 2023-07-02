//
// Copyright (c) 2017 eppz! mobile, Gergely Borbás (SP)
//
// http://www.twitter.com/_eppz
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED,
// INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A
// PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT
// HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF
// CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE
// OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using UnityEngine;
using UnityEditor;
using System.Linq;
using System.Collections.Generic;


namespace EPPZ.Editor
{


	public class SelectByLayer : EditorWindow 
	{


		static int layerIndex;


		[MenuItem("Window/eppz!/Select by Layer")]
		public static void Init()
		{
			SelectByLayer window = EditorWindow.GetWindow<SelectByLayer>("Select by Layer");
			window.Show();
			window.Focus();
		}

		void OnGUI()
		{
			// Layer index.	
			layerIndex = EditorGUILayout.IntField("Layer index", layerIndex);

			if (GUILayout.Button("Select all GameObjects (and Prefabs) on Layer"))
			{ FindAndSelectObjectsByLayer(); }
		}

		public static void FindAndSelectObjectsByLayer()
		{
			// Get all objects.
			GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>().Where(gameObject => gameObject.hideFlags == HideFlags.None).ToArray();

			// Match against layer.
			List<GameObject> matches = new List<GameObject>();
			foreach (GameObject eachGameObject in objects)
			{
				if (eachGameObject.layer == layerIndex)
				{ matches.Add(eachGameObject); }
			}

			// Select.
			Selection.objects = matches.ToArray();
		}
	}
}
