using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TMPro;
using System.Collections;

public class SongSelectManager : MonoBehaviour
{
    [Header("UI 레이어 연결 (Canvas 안의 오브젝트들)")]
    public Image selectScreenImg;
    public Image selectLeftbarImg;
    public Transform songbarContainer; 
    public Image mvIconImg;

    [Header("좌측 곡 정보 UI")]
    public TextMeshProUGUI selectedSongTitleText;  
    public TextMeshProUGUI selectedComposerText;   
    public Image selectedJacketImg;                
    
    [Header("잠긴 곡 UI")]
    public RectTransform lockedStatusPanel;

    [Header("추가 설정 UI")]
    public TextMeshProUGUI speedText;      
    public TextMeshProUGUI mvStatusText;   

    [Header("난이도 텍스트 UI (신규)")]
    public TextMeshProUGUI easyTitleText;
    public TextMeshProUGUI hardTitleText;
    public TextMeshProUGUI insaneTitleText;
    public TextMeshProUGUI easyValueText;
    public TextMeshProUGUI hardValueText;
    public TextMeshProUGUI insaneValueText;

    [Header("기본 에셋 연결")]
    public Sprite arrowSprite;
    public Sprite mvOnSprite;
    public Sprite mvOffSprite;
    public AudioSource previewAudioSource;

    private List<SongData> songList = new List<SongData>();
    private int currentVirtualIndex = 0; 
    private float visualSongIndex = 0f; 
    private bool isMvOn = false;

    private int currentDifficulty = 2; // 1: EASY, 2: HARD, 3: INSANE

    private Color colorEasy;
    private Color colorHard;
    private Color colorInsane;
    private Color colorDisabled;

    private List<Image> spawnedSongbars = new List<Image>();
    private List<Image> spawnedLockIcons = new List<Image>(); 
    
    private Dictionary<int, Sprite> cachedSongbarSprites = new Dictionary<int, Sprite>();
    private Dictionary<int, Sprite> blurImgCache = new Dictionary<int, Sprite>();
    private Dictionary<int, Sprite> leftbarCache = new Dictionary<int, Sprite>();
    private Dictionary<int, Sprite> jacketCache = new Dictionary<int, Sprite>();
    
    private Sprite lockedSprite;

    private Coroutine transitionCoroutine;
    private Coroutine previewCoroutine; 
    private Coroutine lockedPanelCoroutine; 
    
    private GameObject oldBgObj;
    private GameObject oldLeftObj;
    private Vector2 bgOriginalPos;
    private Vector2 leftOriginalPos;
    private bool isPosInitialized = false;
    
    private bool isCurrentlyLocked = false;

    void Start()
    {
        // 난이도 색상 초기화
        ColorUtility.TryParseHtmlString("#5ce1e6", out colorEasy);
        ColorUtility.TryParseHtmlString("#ff751f", out colorHard);
        ColorUtility.TryParseHtmlString("#ff3131", out colorInsane);
        ColorUtility.TryParseHtmlString("#545454", out colorDisabled);

        LoadSongList();
        
        mvOnSprite = Resources.Load<Sprite>("Sprites/GameAsset/MV_ON");
        mvOffSprite = Resources.Load<Sprite>("Sprites/GameAsset/MV_OFF");
        lockedSprite = Resources.Load<Sprite>("Sprites/Songbar/Locked");

        if (songList.Count > 0)
        {
            isCurrentlyLocked = songList[0].ExistSongbar == 2;
            
            if (lockedStatusPanel != null)
            {
                lockedStatusPanel.gameObject.SetActive(true); 
                CanvasGroup cg = lockedStatusPanel.GetComponent<CanvasGroup>();
                if (cg == null) cg = lockedStatusPanel.gameObject.AddComponent<CanvasGroup>();
                
                cg.alpha = isCurrentlyLocked ? 1f : 0f;
            }

            InitializeSongbars();
            ValidateCurrentDifficulty(songList[0]);
            UpdateStaticImages(0); 
            PlayPreviewMusic();
        }
    }

    private void LoadSongList()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "data", "songs.json");
        if (File.Exists(path))
        {
            string json = File.ReadAllText(path, System.Text.Encoding.UTF8);
            SongData[] songArray = JsonHelper.FromJson<SongData>(json);
            songList = songArray.Where(s => s.ExistSongbar == 1 || s.ExistSongbar == 2).ToList();
        }
    }

    private void InitializeSongbars()
    {
        foreach (var song in songList)
        {
            cachedSongbarSprites[song.Id] = Resources.Load<Sprite>($"Sprites/Songbar/{song.Id}");
            blurImgCache[song.Id] = Resources.Load<Sprite>($"Sprites/blurimg/{song.Id}");
            leftbarCache[song.Id] = Resources.Load<Sprite>($"Sprites/SelectLeftbar/{song.Id}");
            jacketCache[song.Id] = Resources.Load<Sprite>($"Sprites/loadimg-large/{song.Id}");
        }

        for (int i = 0; i < 15; i++)
        {
            GameObject barObj = new GameObject($"Songbar_UI_{i}");
            barObj.transform.SetParent(songbarContainer, false);
            Image barImage = barObj.AddComponent<Image>();
            spawnedSongbars.Add(barImage);
            
            GameObject lockObj = new GameObject($"LockIcon_{i}");
            lockObj.transform.SetParent(barObj.transform, false);
            Image lockImage = lockObj.AddComponent<Image>();
            lockImage.sprite = lockedSprite;
            lockImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            lockImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            lockImage.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            lockImage.enabled = false;
            spawnedLockIcons.Add(lockImage);
        }
    }

    private int GetWrappedIndex(int index, int count)
    {
        return ((index % count) + count) % count;
    }

    void Update()
    {
        if (songList.Count == 0) return;

        var kb = Keyboard.current;
        if (kb == null) return;

        bool songChanged = false;
        int slideDirection = 0; 

        if (kb[Key.LeftArrow].wasPressedThisFrame)
        {
            currentVirtualIndex--; 
            songChanged = true;
            slideDirection = -1;
        }
        if (kb[Key.RightArrow].wasPressedThisFrame || kb[Key.D].wasPressedThisFrame)
        {
            currentVirtualIndex++; 
            songChanged = true;
            slideDirection = 1;
        }

        // 난이도 변경 (상/하 방향키)
        if (kb[Key.UpArrow].wasPressedThisFrame) ChangeDifficulty(1);
        if (kb[Key.DownArrow].wasPressedThisFrame) ChangeDifficulty(-1);

        if (kb[Key.A].wasPressedThisFrame)
        {
            CoreManager.Instance.IsAutoPlay = !CoreManager.Instance.IsAutoPlay;
            Debug.Log(CoreManager.Instance.IsAutoPlay ? "AUTO PLAY 개시" : "AUTO PLAY 종료");
        }

        if (kb[Key.F1].wasPressedThisFrame)
        {
            CoreManager.Instance.CurrentSettings.SettingSpeed -= 0.1;
            if (CoreManager.Instance.CurrentSettings.SettingSpeed < 1.0) CoreManager.Instance.CurrentSettings.SettingSpeed = 1.0;
            CoreManager.Instance.CurrentSettings.SettingSpeed = System.Math.Round(CoreManager.Instance.CurrentSettings.SettingSpeed, 1);
        }
        if (kb[Key.F2].wasPressedThisFrame)
        {
            CoreManager.Instance.CurrentSettings.SettingSpeed += 0.1;
            if (CoreManager.Instance.CurrentSettings.SettingSpeed > 12.0) CoreManager.Instance.CurrentSettings.SettingSpeed = 12.0;
            CoreManager.Instance.CurrentSettings.SettingSpeed = System.Math.Round(CoreManager.Instance.CurrentSettings.SettingSpeed, 1);
        }

        int actualDataIndex = GetWrappedIndex(currentVirtualIndex, songList.Count);
        SongData currentSong = songList[actualDataIndex];
        bool hasMv = currentSong.HasMV == 1;
        
        if (hasMv && kb[Key.F].wasPressedThisFrame) isMvOn = !isMvOn;
        else if (!hasMv) isMvOn = false;

        visualSongIndex += (currentVirtualIndex - visualSongIndex) * (10f * Mathf.Min(Time.deltaTime, 0.05f));

        if (songChanged)
        {
            ValidateCurrentDifficulty(currentSong);
            UpdateStaticImages(slideDirection);
            PlayPreviewMusic();
        }

        AnimateSongbars();
        UpdateMvIcon(hasMv);

        if (speedText != null) 
            speedText.text = CoreManager.Instance.CurrentSettings.SettingSpeed.ToString("0.0");
        
        if (mvStatusText != null) 
            mvStatusText.text = hasMv ? (isMvOn ? "ON" : "OFF") : "None";

        if (kb[Key.Enter].wasPressedThisFrame || kb[Key.NumpadEnter].wasPressedThisFrame)
        {
            if (currentSong.ExistSongbar == 2) return;
            
            // 난이도가 전부 00 이면 게임 진입 불가
            if (currentSong.Difficulty_EASY == "00" && currentSong.Difficulty_HARD == "00" && currentSong.Difficulty_INSANE == "00")
                return;

            if (previewAudioSource != null) previewAudioSource.Stop();
            
            if (kb[Key.G].isPressed)
            {
                // 변경된 부분: currentDifficulty를 함께 전달
                if (CoreManager.Instance != null) CoreManager.Instance.LoadEditorScene(currentSong, currentDifficulty);
            }
            else
            {
                if (CoreManager.Instance != null) CoreManager.Instance.LoadGameScene(currentSong, isMvOn, currentDifficulty);
            }
        }
    }

    private void ChangeDifficulty(int direction)
    {
        SongData currentSong = songList[GetWrappedIndex(currentVirtualIndex, songList.Count)];
        int nextDiff = currentDifficulty;

        for (int i = 0; i < 3; i++)
        {
            nextDiff += direction;
            if (nextDiff > 3) nextDiff = 1;
            if (nextDiff < 1) nextDiff = 3;

            if (GetDifficultyValue(currentSong, nextDiff) != "00")
            {
                currentDifficulty = nextDiff;
                break;
            }
        }
        UpdateDifficultyUI(currentSong);
    }

    private void ValidateCurrentDifficulty(SongData song)
    {
        if (GetDifficultyValue(song, currentDifficulty) == "00")
        {
            if (GetDifficultyValue(song, 2) != "00") currentDifficulty = 2; // 기본 HARD
            else if (GetDifficultyValue(song, 1) != "00") currentDifficulty = 1;
            else if (GetDifficultyValue(song, 3) != "00") currentDifficulty = 3;
            else currentDifficulty = 2; // 전체가 00일 때
        }
        UpdateDifficultyUI(song);
    }

    private string GetDifficultyValue(SongData song, int diff)
    {
        if (diff == 1) return song.Difficulty_EASY;
        if (diff == 2) return song.Difficulty_HARD;
        if (diff == 3) return song.Difficulty_INSANE;
        return "00";
    }

    private void UpdateDifficultyUI(SongData song)
    {
        if (easyTitleText == null) return;

        string eVal = song.Difficulty_EASY;
        string hVal = song.Difficulty_HARD;
        string iVal = song.Difficulty_INSANE;

        easyValueText.text = eVal == "00" ? "--" : eVal;
        hardValueText.text = hVal == "00" ? "--" : hVal;
        insaneValueText.text = iVal == "00" ? "--" : iVal;

        easyTitleText.color = eVal == "00" ? colorDisabled : Color.white;
        easyValueText.color = eVal == "00" ? colorDisabled : Color.white;

        hardTitleText.color = hVal == "00" ? colorDisabled : Color.white;
        hardValueText.color = hVal == "00" ? colorDisabled : Color.white;

        insaneTitleText.color = iVal == "00" ? colorDisabled : Color.white;
        insaneValueText.color = iVal == "00" ? colorDisabled : Color.white;

        if (currentDifficulty == 1 && eVal != "00") {
            easyTitleText.color = colorEasy; easyValueText.color = colorEasy;
        } else if (currentDifficulty == 2 && hVal != "00") {
            hardTitleText.color = colorHard; hardValueText.color = colorHard;
        } else if (currentDifficulty == 3 && iVal != "00") {
            insaneTitleText.color = colorInsane; insaneValueText.color = colorInsane;
        }
    }

    private void AnimateSongbars()
    {
        float barSpacing = 110f;  
        float baseX = 750f; 
        float curvature = 20f;    

        int centerVirtual = Mathf.RoundToInt(visualSongIndex);

        for (int i = -7; i <= 7; i++)
        {
            int virtualIdx = centerVirtual + i;
            int uiIdx = GetWrappedIndex(virtualIdx, 15);
            Image barImage = spawnedSongbars[uiIdx];
            Image lockImage = spawnedLockIcons[uiIdx];

            int dataIdx = GetWrappedIndex(virtualIdx, songList.Count);
            int songId = songList[dataIdx].Id;
            bool isLocked = songList[dataIdx].ExistSongbar == 2;

            if (cachedSongbarSprites.TryGetValue(songId, out Sprite spr) && spr != null)
            {
                barImage.sprite = spr;
                barImage.SetNativeSize();
                barImage.rectTransform.localScale = new Vector3(0.4f, 0.4f, 1f);
                barImage.enabled = true;
                
                lockImage.enabled = isLocked;
                if (isLocked) lockImage.rectTransform.sizeDelta = barImage.rectTransform.sizeDelta;
            }
            else
            {
                barImage.enabled = false;
                lockImage.enabled = false;
            }

            float distance = virtualIdx - visualSongIndex;
            float absDistance = Mathf.Abs(distance);

            float yPos = -distance * barSpacing;
            float xPos = baseX + (curvature * absDistance * absDistance);

            barImage.rectTransform.anchoredPosition = new Vector2(xPos, yPos);
            barImage.rectTransform.localRotation = Quaternion.identity;
        
            float smoothFade = Mathf.Max(0f, 1f - (absDistance / 4f));
            float colorVal = (100f + (155f * smoothFade)) / 255f; 
            barImage.color = new Color(colorVal, colorVal, colorVal, 1f);
            
            if (isLocked) lockImage.color = barImage.color;
        }
    }

    private void UpdateStaticImages(int direction)
    {
        int actualDataIndex = GetWrappedIndex(currentVirtualIndex, songList.Count);
        SongData currentSong = songList[actualDataIndex];
        int id = currentSong.Id;
        
        bool checkLocked = currentSong.ExistSongbar == 2;
        if (checkLocked != isCurrentlyLocked)
        {
            isCurrentlyLocked = checkLocked;
            if (lockedPanelCoroutine != null) StopCoroutine(lockedPanelCoroutine);
            lockedPanelCoroutine = StartCoroutine(AnimateLockedPanel(isCurrentlyLocked));
        }
        
        if (!isPosInitialized)
        {
            if (selectScreenImg != null) bgOriginalPos = selectScreenImg.rectTransform.anchoredPosition;
            if (selectLeftbarImg != null) leftOriginalPos = selectLeftbarImg.rectTransform.anchoredPosition;
            isPosInitialized = true;
        }

        if (selectedSongTitleText != null) selectedSongTitleText.text = currentSong.Name;
        if (selectedComposerText != null) selectedComposerText.text = currentSong.Composer;

        if (selectedJacketImg != null)
        {
            if (jacketCache.TryGetValue(id, out Sprite jacket)) { selectedJacketImg.sprite = jacket; selectedJacketImg.color = Color.white; }
            else selectedJacketImg.color = Color.clear;
        }

        if (direction == 0) 
        {
            if (selectScreenImg != null && blurImgCache.TryGetValue(id, out Sprite blur1)) { selectScreenImg.sprite = blur1; selectScreenImg.color = Color.white; }
            if (selectLeftbarImg != null && leftbarCache.TryGetValue(id, out Sprite left1)) { selectLeftbarImg.sprite = left1; selectLeftbarImg.color = Color.white; }
        }
        else 
        {
            if (transitionCoroutine != null) 
            {
                StopCoroutine(transitionCoroutine);
                if (oldBgObj != null) Destroy(oldBgObj);
                if (oldLeftObj != null) Destroy(oldLeftObj);
                if (selectScreenImg != null) selectScreenImg.rectTransform.anchoredPosition = bgOriginalPos;
                if (selectLeftbarImg != null) selectLeftbarImg.rectTransform.anchoredPosition = leftOriginalPos;
            }
            transitionCoroutine = StartCoroutine(SlideTransitionRoutine(id, direction));
        }
    }
    
    private IEnumerator AnimateLockedPanel(bool show)
    {
        if (lockedStatusPanel == null) yield break;
        
        CanvasGroup cg = lockedStatusPanel.GetComponent<CanvasGroup>();
        if (cg == null) yield break;

        float startAlpha = cg.alpha;
        float targetAlpha = show ? 1f : 0f;
        
        float duration = 0.3f;
        float elapsed = 0f;
        
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 3f); 
            
            cg.alpha = Mathf.Lerp(startAlpha, targetAlpha, ease);
            yield return null;
        }
        
        cg.alpha = targetAlpha;
    }

    private IEnumerator SlideTransitionRoutine(int newId, int direction)
    {
        float slideDistance = selectScreenImg != null ? selectScreenImg.rectTransform.rect.height : 1080f;
        if (slideDistance <= 0) slideDistance = 1080f;

        float duration = 0.25f; 
        float offset = direction == 1 ? -slideDistance : slideDistance; 

        if (selectScreenImg != null)
        {
            oldBgObj = new GameObject("OldBgObj");
            oldBgObj.transform.SetParent(selectScreenImg.transform.parent, false);
            oldBgObj.transform.SetSiblingIndex(selectScreenImg.transform.GetSiblingIndex());
            Image oldBgImg = oldBgObj.AddComponent<Image>();
            oldBgImg.sprite = selectScreenImg.sprite;
            oldBgImg.color = selectScreenImg.color;
            RectTransform oldBgRect = oldBgObj.GetComponent<RectTransform>();
            CopyRectSettings(selectScreenImg.rectTransform, oldBgRect);
            
            if (blurImgCache.TryGetValue(newId, out Sprite newBlur)) selectScreenImg.sprite = newBlur;
        }

        if (selectLeftbarImg != null)
        {
            oldLeftObj = new GameObject("OldLeftObj");
            oldLeftObj.transform.SetParent(selectLeftbarImg.transform.parent, false);
            oldLeftObj.transform.SetSiblingIndex(selectLeftbarImg.transform.GetSiblingIndex());
            Image oldLeftImg = oldLeftObj.AddComponent<Image>();
            oldLeftImg.sprite = selectLeftbarImg.sprite;
            oldLeftImg.color = selectLeftbarImg.color;
            RectTransform oldLeftRect = oldLeftObj.GetComponent<RectTransform>();
            CopyRectSettings(selectLeftbarImg.rectTransform, oldLeftRect);

            if (leftbarCache.TryGetValue(newId, out Sprite newLeft)) selectLeftbarImg.sprite = newLeft;
        }

        if (selectScreenImg != null) selectScreenImg.rectTransform.anchoredPosition = bgOriginalPos + new Vector2(0, offset);
        if (selectLeftbarImg != null) selectLeftbarImg.rectTransform.anchoredPosition = leftOriginalPos + new Vector2(0, offset);

        RectTransform oBgRect = oldBgObj != null ? oldBgObj.GetComponent<RectTransform>() : null;
        RectTransform oLeftRect = oldLeftObj != null ? oldLeftObj.GetComponent<RectTransform>() : null;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = elapsed / duration;
            float ease = 1f - Mathf.Pow(1f - t, 3f); 

            if (selectScreenImg != null) selectScreenImg.rectTransform.anchoredPosition = Vector2.Lerp(bgOriginalPos + new Vector2(0, offset), bgOriginalPos, ease);
            if (oBgRect != null) oBgRect.anchoredPosition = Vector2.Lerp(bgOriginalPos, bgOriginalPos + new Vector2(0, -offset), ease);

            if (selectLeftbarImg != null) selectLeftbarImg.rectTransform.anchoredPosition = Vector2.Lerp(leftOriginalPos + new Vector2(0, offset), leftOriginalPos, ease);
            if (oLeftRect != null) oLeftRect.anchoredPosition = Vector2.Lerp(leftOriginalPos, leftOriginalPos + new Vector2(0, -offset), ease);

            yield return null;
        }

        if (selectScreenImg != null) selectScreenImg.rectTransform.anchoredPosition = bgOriginalPos;
        if (selectLeftbarImg != null) selectLeftbarImg.rectTransform.anchoredPosition = leftOriginalPos;

        if (oldBgObj != null) { Destroy(oldBgObj); oldBgObj = null; }
        if (oldLeftObj != null) { Destroy(oldLeftObj); oldLeftObj = null; }

        transitionCoroutine = null;
    }

    private void CopyRectSettings(RectTransform source, RectTransform target)
    {
        target.anchorMin = source.anchorMin;
        target.anchorMax = source.anchorMax;
        target.pivot = source.pivot;
        target.sizeDelta = source.sizeDelta;
        target.anchoredPosition = source.anchoredPosition;
    }

    private void UpdateMvIcon(bool hasMv)
    {
        if (mvIconImg == null) return;
        if (hasMv) {
            mvIconImg.enabled = true;
            mvIconImg.sprite = isMvOn ? mvOnSprite : mvOffSprite;
            mvIconImg.rectTransform.anchoredPosition = new Vector2(-960 + 275, -540 + 100); 
            mvIconImg.rectTransform.sizeDelta = new Vector2(450, 100);
        } else mvIconImg.enabled = false;
    }

    private void PlayPreviewMusic()
    {
        if (previewAudioSource == null || songList.Count == 0) return;
        previewAudioSource.Stop();
        
        if (previewCoroutine != null) StopCoroutine(previewCoroutine);
        previewCoroutine = StartCoroutine(LoadAndPlayPreview());
    }

    private IEnumerator LoadAndPlayPreview()
    {
        yield return new WaitForSeconds(0.15f);

        int actualDataIndex = GetWrappedIndex(currentVirtualIndex, songList.Count);
        int songId = songList[actualDataIndex].Id;

        ResourceRequest request = Resources.LoadAsync<AudioClip>($"Audio/Preview/{songId}");
        yield return request;

        int currentIndexNow = GetWrappedIndex(currentVirtualIndex, songList.Count);
        if (songList[currentIndexNow].Id != songId) yield break;

        if (request.asset != null) 
        { 
            previewAudioSource.clip = request.asset as AudioClip;
            previewAudioSource.volume = 1f;
            previewAudioSource.time = 0f;
            previewAudioSource.loop = true; 
            previewAudioSource.Play(); 
        }
    }
}