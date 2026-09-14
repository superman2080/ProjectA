using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CameraController : MonoBehaviour
{

	public Transform origin;
	public float sensitivity = 5;

	private Vector2 velocity;

    // Start is called before the first frame update
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
		if (Input.GetButton("Fire1")) {
			velocity.x += Input.GetAxis("Mouse X") * sensitivity * Time.deltaTime;
			velocity.y -= Input.GetAxis("Mouse Y") * sensitivity * Time.deltaTime;
		}
		transform.RotateAround(origin.position, transform.right, velocity.y);
		transform.RotateAround(origin.position, Vector3.up, velocity.x);
		transform.Translate(Vector3.forward * Input.GetAxis("Mouse ScrollWheel") * 10);
		velocity.x = Mathf.Lerp(velocity.x, 0, Time.deltaTime * 3);
		velocity.y = Mathf.Lerp(velocity.y, 0, Time.deltaTime * 3);
	}
}
