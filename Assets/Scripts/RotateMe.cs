using UnityEngine;

public class RotateMe : MonoBehaviour
{
	public float speed = 50.0f;
	
    void Update()
    {
        Transform t = GetComponent<Transform>();
        t.Rotate(new Vector3(0, Time.deltaTime * speed, 0));
    }
}
