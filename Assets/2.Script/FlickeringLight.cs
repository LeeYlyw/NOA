using System.Collections;
using UnityEngine;

public class FlickeringLight : MonoBehaviour
{
    [SerializeField] private Light targetLight;
    [SerializeField] private float interval = 3f;

    private void Awake()
    {
        if (targetLight == null)
            targetLight = GetComponentInChildren<Light>();
    }

    private void OnEnable()
    {
        if (targetLight != null)
            StartCoroutine(FlickerRoutine());
    }

    private IEnumerator FlickerRoutine()
    {
        targetLight.enabled = false;

        // 여러 전등이 동시에 깜빡이지 않도록 시작 시점만 다르게 한다.
        yield return new WaitForSeconds(Random.Range(0f, interval));

        while (true)
        {
            targetLight.enabled = true;
            yield return new WaitForSeconds(0.08f);

            targetLight.enabled = false;
            yield return new WaitForSeconds(0.10f);

            targetLight.enabled = true;
            yield return new WaitForSeconds(0.18f);

            targetLight.enabled = false;
            yield return new WaitForSeconds(Mathf.Max(0.1f, interval - 0.36f));
        }
    }

    private void OnDisable()
    {
        if (targetLight != null)
            targetLight.enabled = false;
    }
}