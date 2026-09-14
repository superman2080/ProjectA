using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

public class CrackParameterHandler : MonoBehaviour
{
	public MeshRenderer cracksRenderer;

	public InputField oXField;
	public InputField oYField;
	public InputField thicknessField;
	public InputField sizeField;

	public bool[] animateFields = { false, false };
	public bool menuShown = true;

	private Vector4 offset = Vector4.zero;

	void Update() {
		bool animate = false;
		for (int i = 0; i < animateFields.Length; i++) {
			if (animateFields[i] == true) {
				animate = true;
				break;
			}
		}
		if (animate) {
			if (animateFields[0]) cracksRenderer.material.SetFloat("_CrackThickness", Mathf.Sin(Time.time) * 0.1f + 0.1f);
			if (animateFields[1]) cracksRenderer.material.SetInt("_CrackAmount", (int)(Mathf.Sin(Time.time) * 32 + 64));
			PopulateFields();
		}

		if (Input.GetKeyDown(KeyCode.Q)) {
			menuShown = !menuShown;
			GetComponent<Canvas>().enabled = menuShown;
		}

		if (menuShown) {
			Camera.main.transform.localPosition = Vector3.Lerp(Camera.main.transform.localPosition, new Vector3(-1.27f, 0, 0), Time.deltaTime * 5);
		} else {
			Camera.main.transform.localPosition = Vector3.Lerp(Camera.main.transform.localPosition, Vector3.zero, Time.deltaTime * 5);
		}
	}

	public void ToggleAnimateThickness(bool toggle) {
		animateFields[0] = toggle;
	}

	public void ToggleAnimateSize(bool toggle) {
		animateFields[1] = toggle;
	}

	public void PopulateFields() {
		thicknessField.text = cracksRenderer.material.GetFloat("_CrackThickness").ToString();
		sizeField.text = cracksRenderer.material.GetFloat("_CrackAmount").ToString();
	}

	public void UpdateThickness(string input) {
		float newThickness = 0;
		if (!float.TryParse(input, out newThickness)) {
			ErrorOut("Thickness must be a float value.");
		}

		cracksRenderer.material.SetFloat("_CrackThickness", newThickness);
	}

	public void UpdateSize(string input) {
		int newSize = 0;
		if (!int.TryParse(input, out newSize)) {
			ErrorOut("Size must be a float value.");
		}

		cracksRenderer.material.SetInt("_CrackAmount", newSize);
	}

	public void UpdateOffsetX(string input) {
		float newX = 0;
		if (!float.TryParse(input, out newX)) {
			ErrorOut("Offset X must be a float value.");
		}

		offset.x = newX;
		cracksRenderer.material.SetVector("_NoiseOffset", offset);
	}

	public void UpdateOffsetY(string input) {
		float newY = 0;
		if (!float.TryParse(input, out newY)) {
			ErrorOut("Offset Y must be a float value.");
		}

		offset.y = newY;
		cracksRenderer.material.SetVector("_NoiseOffset", offset);
	}

	public void ErrorOut(string msg) {
		Debug.LogError(msg);
	}
}
