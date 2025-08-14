using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace AnimatedEmojiDemo
{
    public class Demo : MonoBehaviour
    {
        [SerializeField] private TextMeshProUGUI smileName;
        [SerializeField] private Transform creationRoot;
        [SerializeField] private List<GameObject> emojies = new List<GameObject>();

        private IEnumerator Start()
        {
            var currentIndex = 0;
            while (emojies.Count > 0)
            {
                var currentCreatedSmileIndex = currentIndex;
                var instance = Instantiate(emojies[currentIndex], creationRoot, true);
                instance.transform.localScale = Vector3.one;
                instance.transform.localPosition = Vector3.zero;
                instance.transform.localRotation = Quaternion.identity;

                smileName.text = emojies[currentIndex].name;

                var remainingTime = instance.GetComponent<Animator>().GetCurrentAnimatorStateInfo(0).length;

                while (remainingTime > 0.0f &&
                       currentCreatedSmileIndex == currentIndex)
                {
                    if (Input.GetKeyDown(KeyCode.LeftArrow))
                        currentIndex--;

                    if (Input.GetKeyDown(KeyCode.RightArrow))
                        currentIndex++;

                    remainingTime -= Time.deltaTime;

                    yield return null;
                }

                if (currentCreatedSmileIndex == currentIndex)
                    currentIndex++;
                
                Destroy(instance);

                currentIndex = (int)Mathf.Repeat(currentIndex, emojies.Count);
            }
        }
    }
}