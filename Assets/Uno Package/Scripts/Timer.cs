using UnityEngine;
using System.Collections;

public class Timer
{
    public delegate void Task();

    public static void Schedule(MonoBehaviour behaviour, float delay, Task task)
    {
        behaviour.StartCoroutine(DoTask(task, delay));
    }

    private static IEnumerator DoTask(Task task, float delay)
    {
        yield return new WaitForSeconds(delay);
        task?.Invoke();
    }
}
