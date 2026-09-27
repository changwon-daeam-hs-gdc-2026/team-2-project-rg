using System.Collections.Generic;
using UnityEngine;

public class NoteObject : MonoBehaviour
{
    public NoteData myData;
    private SpriteRenderer spriteRenderer;
    private GameObject myLongBody;
    private SpriteRenderer bodyRenderer;
    
    public bool isMissed = false; 

    private float[] laneXPositions = { -1.2f, -0.4f, 0.4f, 1.2f }; 

    [Header("기본 에셋 설정")]
    public Sprite blueShort; public Sprite greenShort;
    public Sprite blueLongHead; public Sprite greenLongHead;
    public Sprite blueLongBody; public Sprite greenLongBody;
    public Sprite blueLongTail; public Sprite greenLongTail;

    private double myTargetDist;
    private double myNextTargetDist;
    private double myTailTime = 0.0;
    
    private float screenW, screenH;
    private Vector3 originalScale;

    private static Dictionary<string, Sprite> redNoteSprites = null;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale; 
        
        if (Camera.main != null) {
            screenH = Camera.main.orthographicSize * 2f;
            screenW = screenH * Camera.main.aspect;
        }

        if (redNoteSprites == null)
        {
            redNoteSprites = new Dictionary<string, Sprite>();
            Sprite[] sprites = Resources.LoadAll<Sprite>("Sprites/NoteAsset/red_Note");
            foreach (Sprite s in sprites) {
                redNoteSprites[s.name] = s;
            }
        }
    }

    public void Initialize(NoteData data)
    {
        myData = data;
        isMissed = false;
        spriteRenderer.color = Color.white;
        spriteRenderer.enabled = true;
        spriteRenderer.sortingOrder = 5; 
        spriteRenderer.flipY = false;

        if (myData.IsFreeKey)
        {
            if (myData.FreeKeyType == 0) spriteRenderer.sprite = GameManager.Instance.freeKeyNormalSprite;
            else if (myData.FreeKeyType == 1) spriteRenderer.sprite = GameManager.Instance.freeKeySpaceSprite;
            else if (myData.FreeKeyType == 2) spriteRenderer.sprite = GameManager.Instance.freeKeyDragSprite;
            
            transform.localScale = new Vector3(1.2f, 1.2f, 1f); 
        }
        else if (myData.IsRedNote)
        {
            string prefix = $"{myData.Width}x";
            string targetSpriteName = "";

            if (myData.Type == 11) targetSpriteName = $"{prefix}_Basic";
            else if (myData.Type == 12 || myData.Type == 13) targetSpriteName = $"{prefix}_longhead";

            if (myData.Type == 13) spriteRenderer.flipY = true;
            
            float laneWidthGame = 0.8f; 
            float targetW = (myData.Width - 1) * laneWidthGame + (laneWidthGame * 0.95f);

            if (redNoteSprites.TryGetValue(targetSpriteName, out Sprite foundSprite)) {
                spriteRenderer.sprite = foundSprite;
                float currentW = foundSprite.bounds.size.x;
                float dynamicScale = targetW / currentW;
                
                transform.localScale = new Vector3(dynamicScale, dynamicScale, 1f);
            } else {
                Texture2D tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.red); tex.Apply();
                spriteRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                spriteRenderer.color = new Color(1f, 0.2f, 0.2f, 0.8f);
                transform.localScale = new Vector3(targetW, 0.3f, 1f);
            }

            transform.rotation = Quaternion.identity;

            if (myData.Type == 12 && myData.NextLongNote != null)
            {
                if (myLongBody == null) {
                    myLongBody = new GameObject("LongBody");
                    myLongBody.transform.SetParent(this.transform);
                    bodyRenderer = myLongBody.AddComponent<SpriteRenderer>();
                    bodyRenderer.sortingOrder = 2; 
                }
                myLongBody.SetActive(true);
                bodyRenderer.color = Color.white;
                bodyRenderer.enabled = true;

                string midSpriteName = $"{prefix}_mid";
                if (redNoteSprites.TryGetValue(midSpriteName, out Sprite midSprite)) {
                    bodyRenderer.sprite = midSprite;
                } else {
                    Texture2D tex = new Texture2D(1, 1); tex.SetPixel(0, 0, Color.red); tex.Apply();
                    bodyRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
                    bodyRenderer.color = new Color(1f, 0.2f, 0.2f, 0.5f);
                }
            }
            else
            {
                if (myLongBody != null) myLongBody.SetActive(false); 
            }
        }
        else if (myData.Type == 4) 
        {
            // ✅ GameManager의 마디선 생성 방식과 완전히 동일한 텍스처와 색상 적용
            Texture2D tex = new Texture2D(1, 1); 
            tex.SetPixel(0, 0, new Color(128, 128, 128, 0.2f)); 
            tex.Apply();
            
            spriteRenderer.sprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            spriteRenderer.color = Color.white; 
            
            // ✅ 노트(5)보다 아래에 깔리도록 렌더링 오더를 1로 지정
            spriteRenderer.sortingOrder = 1; 
            
            float laneWidthGame = 0.8f;
            float totalWidth = Mathf.Abs(laneXPositions[laneXPositions.Length - 1] - laneXPositions[0]) + laneWidthGame;
            
            transform.localScale = new Vector3(totalWidth, 0.015f, 1f);
            transform.rotation = Quaternion.identity;
            
            if (myLongBody != null) myLongBody.SetActive(false);
        }
        else
        {
            bool isBlue = (myData.Lane == 0 || myData.Lane == 3);
            if (myData.Type == 1) spriteRenderer.sprite = isBlue ? blueShort : greenShort;
            else if (myData.Type == 2) spriteRenderer.sprite = isBlue ? blueLongHead : greenLongHead;
            else if (myData.Type == 3) spriteRenderer.sprite = isBlue ? blueLongTail : greenLongTail;
            transform.rotation = Quaternion.identity;
            
            transform.localScale = originalScale; 

            if (myData.Type == 2 && myData.NextLongNote != null)
            {
                if (myLongBody == null) {
                    myLongBody = new GameObject("LongBody");
                    myLongBody.transform.SetParent(this.transform);
                    bodyRenderer = myLongBody.AddComponent<SpriteRenderer>();
                    bodyRenderer.sortingOrder = 2; 
                }
                myLongBody.SetActive(true);
                bodyRenderer.color = Color.white;
                bodyRenderer.enabled = true;
                bodyRenderer.sprite = isBlue ? blueLongBody : greenLongBody;
            }
            else
            {
                if (myLongBody != null) myLongBody.SetActive(false); 
            }
        }

        myTargetDist = GameManager.Instance.GetScrollDistance(myData.HitTime);
        if (myData.NextLongNote != null) myNextTargetDist = GameManager.Instance.GetScrollDistance(myData.NextLongNote.HitTime);

        myTailTime = ((myData.Type == 2 || myData.Type == 12) && myData.NextLongNote != null) ? myData.NextLongNote.HitTime : myData.HitTime;

        UpdateTransform();
    }

    private void UpdateTransform()
    {
        double currentDist = GameManager.Instance.currentScrollDist;
        double currentTime = GameManager.Instance.currentPlayTime;

        bool isHidden = GameManager.Instance.IsNoteHiddenByFutureReverseScroll(currentTime, myData.HitTime);

        if (myData.IsFreeKey)
        {
            float distRemFloat = (float)(myTargetDist - currentDist) * 0.01f;

            if (distRemFloat > 25f || isHidden)
            {
                spriteRenderer.enabled = false;
                return;
            }
            spriteRenderer.enabled = true;

            Vector3 linePos = Vector3.zero;
            Quaternion lineRot = Quaternion.identity;

            if (GameManager.Instance.activeLines.ContainsKey(myData.LineNumber)) {
                var lineObj = GameManager.Instance.activeLines[myData.LineNumber].go;
                if (lineObj != null) {
                    linePos = lineObj.transform.position;
                    lineRot = lineObj.transform.rotation;
                }
            }

            float startOffset, targetOffset;
            if (myData.Direction == "Top") {
                startOffset = ((float)myData.StartPos / 100f) * (screenW / 2f);
                targetOffset = ((float)myData.TargetPos / 100f) * (screenW / 2f);
            } else {
                startOffset = ((float)myData.StartPos / 100f) * (screenH / 2f);
                targetOffset = ((float)myData.TargetPos / 100f) * (screenH / 2f);
            }

            float appearDist = (myData.Direction == "Top") ? 8.0f : 10.0f;
            float p = Mathf.Clamp01(distRemFloat / appearDist); 
            float currentOffset = Mathf.Lerp(targetOffset, startOffset, p);

            float localCurrentX = 0f;
            float localCurrentY = 0f;

            if (myData.Direction == "Top") {
                localCurrentX = currentOffset;
                localCurrentY = distRemFloat; 
            } else if (myData.Direction == "Right") {
                localCurrentX = distRemFloat; 
                localCurrentY = currentOffset;
            } else if (myData.Direction == "Left") {
                localCurrentX = -distRemFloat; 
                localCurrentY = currentOffset;
            }

            Vector3 localPos = new Vector3(localCurrentX, localCurrentY, 0f);
            transform.position = linePos + (lineRot * localPos);
            transform.rotation = lineRot * Quaternion.Euler(0, 0, (float)myData.Rotation);
        }
        else
        {
            bool isLongNote = (myData.Type == 2 || myData.Type == 12) && (myData.NextLongNote != null);

            if (isHidden)
            {
                spriteRenderer.enabled = false;
                if (myLongBody != null && myLongBody.activeSelf) myLongBody.SetActive(false);
                return; 
            }
            else
            {
                if (myData.IsHit && !isMissed)
                {
                    spriteRenderer.enabled = false;
                    if (myLongBody != null && isLongNote && !myLongBody.activeSelf) myLongBody.SetActive(true);
                }
                else
                {
                    spriteRenderer.enabled = true;
                    if (myLongBody != null && isLongNote && !myLongBody.activeSelf) myLongBody.SetActive(true);
                }
            }

            float rawYPos = GameManager.Instance.hitLineY + (float)(myTargetDist - currentDist) * 0.01f;
            float yPos = rawYPos;
            
            if (myData.IsHit && !isMissed && isLongNote)
            {
                float gapOffset = 0.1f; 
                if (yPos < GameManager.Instance.hitLineY - gapOffset) 
                    yPos = GameManager.Instance.hitLineY - gapOffset;
            }
            
            if (myData.Type == 4)
            {
                float startX = laneXPositions[0];
                float endX = laneXPositions[laneXPositions.Length - 1];
                float centerX = (startX + endX) / 2f;
                transform.position = new Vector3(centerX, rawYPos, 0f);
            }
            else
            {
                float startX = laneXPositions[myData.Lane];
                int endLaneClamp = Mathf.Min(myData.Lane + myData.Width - 1, laneXPositions.Length - 1);
                float endX = laneXPositions[endLaneClamp];
                float centerX = (startX + endX) / 2f;

                transform.position = new Vector3(centerX, rawYPos, 0f);

                if (myLongBody != null && myLongBody.activeSelf && isLongNote)
                {
                    float endYPos = GameManager.Instance.hitLineY + (float)(myNextTargetDist - currentDist) * 0.01f;
                    
                    if (endYPos <= GameManager.Instance.hitLineY)
                    {
                        myLongBody.SetActive(false);
                    }
                    else
                    {
                        float distance = endYPos - yPos;
                        if (distance > 0 && bodyRenderer != null && bodyRenderer.sprite != null)
                        {
                            float parentScaleY = transform.localScale.y; 
                            float spriteHeight = bodyRenderer.sprite.bounds.size.y;
                            
                            myLongBody.transform.position = new Vector3(centerX, (yPos + endYPos) / 2f, 0f);
                            float finalScaleY = (distance / (spriteHeight * parentScaleY));

                            float laneWidthGame = 0.8f;
                            float targetW = (myData.Width - 1) * laneWidthGame + (laneWidthGame * 0.95f);
                            float bodyScaleX = targetW / (bodyRenderer.sprite.bounds.size.x * transform.localScale.x);

                            myLongBody.transform.localScale = new Vector3(bodyScaleX, finalScaleY, 1f);
                        }
                        else
                        {
                            myLongBody.SetActive(false);
                        }
                    }
                }
            }
        }
    }

    void LateUpdate()
    {
        if (myData == null) return;

        if (myData.IsHit && !isMissed)
        {
            if (myData.IsFreeKey || myData.Type == 1 || myData.Type == 3 || myData.Type == 11 || myData.Type == 13 || myData.Type == 4) 
            {
                GameManager.Instance.ReturnNoteToPool(this); 
                return;
            }
            else if ((myData.Type == 2 || myData.Type == 12) && spriteRenderer.enabled) 
            {
                spriteRenderer.enabled = false; 
            }
        }

        UpdateTransform();

        double currentTime = GameManager.Instance.currentPlayTime;

        if (currentTime - myData.HitTime > GameManager.W_BAD && !myData.IsHit)
        {
            if (myData.IsFreeKey || myData.Type == 1 || myData.Type == 11) 
            {
                GameManager.Instance.ProcessHit("MISS");
                DimNote(); 
            }
            else if (myData.Type == 2 || myData.Type == 12) 
            {
                GameManager.Instance.MissLongNoteHead(myData); 
            }
            else if (myData.Type == 4)
            {
                myData.IsHit = true; 
            }
        }

        if (currentTime - myTailTime > 1.5) GameManager.Instance.ReturnNoteToPool(this);
    }

    public void HideNote() {
        if (spriteRenderer) spriteRenderer.enabled = false;
        if (bodyRenderer) bodyRenderer.enabled = false;
    }

    public void DimNote() {
        isMissed = true;
        myData.IsHit = true; 
        Color darkGray = new Color(0.3f, 0.3f, 0.3f, 1f); 
        if (spriteRenderer) spriteRenderer.color = darkGray;
        if (bodyRenderer) bodyRenderer.color = darkGray;
    }
}