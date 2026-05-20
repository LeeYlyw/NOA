using UnityEngine;

public class RotateItem : MonoBehaviour
{
    public float rotateSpeed = 50f; // 회전 속도


    private Vector3 startPos;

    void Start()
    {
        startPos = transform.position;
    }

    void Update()
    {
        // 1. 아이템을 제자리에서 빙글빙글 회전시킴
        transform.Rotate(Vector3.up * rotateSpeed * Time.deltaTime);

      
    }
}