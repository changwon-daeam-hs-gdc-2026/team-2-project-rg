using System.Collections.Generic;
using UnityEngine;

public class NoteObject : MonoBehaviour
{
    public NoteData myData;
    private GameManager owner; // 이 노트를 만든 GameManager (재시작/다른 판의 매니저와 섞이지 않게)
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

    // 곡 종료 / FAILED 때 함께 투명해지기 위한 원래 색
    private Color headBaseColor = Color.white;
    private Color bodyBaseColor = Color.white;
    private float appliedEndAlpha = 1f;

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
        owner = GameManager.Instance;
        isMissed = false;
        spriteRenderer.color = Color.white;
        spriteRenderer.enabled = true;
        spriteRenderer.sortingOrder = NoteLayer.HeadOrder(myData.Layer); 
        spriteRenderer.flipY = false;
        spriteRenderer.drawMode = SpriteDrawMode.Simple; // 풀링 재사용 시 SIMPLE 스킨의 Sliced 상태 해제

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
                bodyRenderer.sortingOrder = NoteLayer.BodyOrder(myData.Layer);

                string midSpriteName = $"{prefix}_mid";
                if (redNoteSprites.TryGetValue(midSpriteName, out Sprite midSprite)) {
                    // 9-slice 몸통: 양 끝 영역은 고정, 가운데만 늘어남 (길이가 길어져도 끝이 투명하게 번지지 않음)
                    bodyRenderer.drawMode = SpriteDrawMode.Simple;
                    bodyRenderer.sprite = LongBodyHelper.GetSlicedSprite(midSprite);
                    bodyRenderer.drawMode = SpriteDrawMode.Sliced;
                } else {
                    bodyRenderer.drawMode = SpriteDrawMode.Simple;
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
                bodyRenderer.sortingOrder = NoteLayer.BodyOrder(myData.Layer);
                bodyRenderer.drawMode = SpriteDrawMode.Simple; // 풀링 재사용 시 이전 RED 몸통의 Sliced 상태 해제
                bodyRenderer.sprite = isBlue ? blueLongBody : greenLongBody;
            }
            else
            {
                if (myLongBody != null) myLongBody.SetActive(false); 
            }
        }

        // 설정 > GAME UI 가 SIMPLE 이면 단색 둥근 노트로 바꾼다 (프리키 노트 / 마디선은 기본 그대로)
        if (SimpleGameSkin.Active != null && !myData.IsFreeKey && myData.Type != 4) ApplySimpleStyle();


        // 판정선 아래로는 그리지 않음 (GameManager 의 NoteClipMask). 프리키 노트는 자유롭게 움직이므로 제외
        SpriteMaskInteraction clip = myData.IsFreeKey ? SpriteMaskInteraction.None : SpriteMaskInteraction.VisibleInsideMask;
        spriteRenderer.maskInteraction = clip;
        if (bodyRenderer != null) bodyRenderer.maskInteraction = clip;

        // SIMPLE-DIVERSE: 레인 노트는 원근 재질(판정선 아래 자르기 포함), 그 밖에는 원래 재질 (풀링 재사용 대비 매번 지정)
        if (baseMaterial == null) baseMaterial = spriteRenderer.sharedMaterial;
        if (bodyRenderer != null && baseBodyMaterial == null) baseBodyMaterial = bodyRenderer.sharedMaterial;
        DiversePlayfield pf = SimpleGameSkin.Active != null ? SimpleGameSkin.Active.Playfield : null;
        if (pf != null && !myData.IsFreeKey)
        {
            pf.Apply(spriteRenderer, true);
            if (bodyRenderer != null) pf.Apply(bodyRenderer, true);
        }
        else
        {
            if (spriteRenderer.sharedMaterial != baseMaterial) spriteRenderer.sharedMaterial = baseMaterial;
            if (bodyRenderer != null && baseBodyMaterial != null && bodyRenderer.sharedMaterial != baseBodyMaterial) bodyRenderer.sharedMaterial = baseBodyMaterial;
        }

        RememberBaseColors();

        // 층별 스크롤 속도: 노트가 속한 층의 변속 기준으로 위치를 계산한다 (롱노트 머리/꼬리는 항상 같은 층)
        myTargetDist = GameManager.Instance.GetScrollDistance(myData.HitTime, myData.Layer);
        if (myData.NextLongNote != null) myNextTargetDist = GameManager.Instance.GetScrollDistance(myData.NextLongNote.HitTime, myData.Layer);

        myTailTime = ((myData.Type == 2 || myData.Type == 12) && myData.NextLongNote != null) ? myData.NextLongNote.HitTime : myData.HitTime;

        UpdateTransform();
    }

    private void UpdateTransform()
    {
        double currentDist = GameManager.Instance.GetCurrentScrollDist(myData.Layer);
        double currentTime = GameManager.Instance.currentPlayTime;

        bool isHidden = GameManager.Instance.IsNoteHiddenByFutureReverseScroll(currentTime, myData.HitTime, myData.Layer);

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
                            float laneWidthGame = 0.8f;
                            float targetW = (myData.Width - 1) * laneWidthGame + (laneWidthGame * 0.95f);

                            if (bodyRenderer.drawMode == SpriteDrawMode.Tiled)
                            {
                                // SIMPLE RED 몸통: 줄무늬 타일이 늘어나지 않고 반복되도록 크기(size)로 맞춘다
                                Vector3 ps = transform.lossyScale;
                                myLongBody.transform.position = new Vector3(centerX, (yPos + endYPos) * 0.5f, 0f);
                                myLongBody.transform.localScale = new Vector3(1f / Mathf.Max(0.0001f, Mathf.Abs(ps.x)), 1f / Mathf.Max(0.0001f, Mathf.Abs(ps.y)), 1f);
                                bodyRenderer.size = new Vector2(targetW, distance);
                            }
                            else if (bodyRenderer.drawMode == SpriteDrawMode.Sliced)
                            {
                                // RED 몸통: 머리 중심 ~ 꼬리 중심 구간에 메쉬 크기를 정확히 맞춘다 (9-slice)
                                LongBodyHelper.Fit(bodyRenderer, centerX, yPos, endYPos, targetW, 0f);
                            }
                            else
                            {
                            // [버그 수정] 몸통을 "머리 중심 ~ 꼬리 중심" 구간에 정확히 맞춘다.
                            //  - 스프라이트 피벗이 중앙이 아니어도(bounds.center != 0) 구간 밖으로 밀려 나가지 않도록 보정
                            //  - 부모(노트) 스케일에 의존하지 않고 '월드 기준 목표 크기'에서 로컬 스케일을 역산
                            Bounds sb = bodyRenderer.sprite.bounds;
                            float parentSX = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.x));
                            float parentSY = Mathf.Max(0.0001f, Mathf.Abs(transform.lossyScale.y));

                            float worldScaleX = targetW / Mathf.Max(0.0001f, sb.size.x);
                            float worldScaleY = distance / Mathf.Max(0.0001f, sb.size.y);

                            float midY = (yPos + endYPos) / 2f;
                            myLongBody.transform.position = new Vector3(
                                centerX - sb.center.x * worldScaleX,
                                midY - sb.center.y * worldScaleY,
                                0f);
                            myLongBody.transform.localScale = new Vector3(worldScaleX / parentSX, worldScaleY / parentSY, 1f);
                            }
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
        // 이전 판(다른 GameManager)에서 남은 노트는 즉시 제거한다. (재시도/여러 판 플레이 시 유령 노트 방지)
        if (owner == null || GameManager.Instance != owner)
        {
            Destroy(gameObject);
            return;
        }

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
        ApplyEndAlpha();

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

    // SIMPLE 스킨 노트: 둥근 사각형(9-slice), 레인 색 / 빨간 노트, 꼬리는 얇게, 몸통은 반투명 단색
    private Material baseMaterial, baseBodyMaterial;

    private void ApplySimpleStyle()
    {
        SimpleGameSkin skin = SimpleGameSkin.Active;
        float laneWidthGame = 0.8f;
        float w = (myData.IsRedNote ? (myData.Width - 1) * laneWidthGame : 0f) + laneWidthGame * 0.95f;
        bool isTail = myData.Type == 3 || myData.Type == 13;
        Color c = SimpleGameSkin.NoteColor(myData);

        spriteRenderer.drawMode = SpriteDrawMode.Sliced;
        float h = myData.IsRedNote
            ? (isTail ? SimpleGameSkin.RedTailHeight : SimpleGameSkin.RedNoteHeight)  // 빨간 노트는 조금 더 두껍게
            : (isTail ? SimpleGameSkin.TailHeight : SimpleGameSkin.NoteHeight);
        if (myData.IsRedNote && skin.RedNoteSprite != null)
        {
            // 고급 RED 노트: 색이 스프라이트에 들어 있으므로 흰색으로, 바깥 빛 여백만큼 크게 그림
            float pad = SimpleGameSkin.RedGlowPad * 2f;
            spriteRenderer.sprite = skin.RedNoteSprite;
            spriteRenderer.size = new Vector2(w + pad, h + pad);
            spriteRenderer.color = isTail ? new Color(1f, 1f, 1f, 0.9f) : Color.white;
        }
        else
        {
            spriteRenderer.sprite = skin.NoteSprite;
            spriteRenderer.size = new Vector2(w, h);
            spriteRenderer.color = isTail ? new Color(c.r, c.g, c.b, 0.9f) : c;
        }
        spriteRenderer.flipY = false;
        transform.localScale = new Vector3(1f, skin.NoteScaleY, 1f); // DIVERSE 는 세로로 조금 두껍게 (몸통은 부모 배율을 되돌려 계산)
        transform.rotation = Quaternion.identity;

        if (myLongBody != null && myLongBody.activeSelf && bodyRenderer != null)
        {
            bodyRenderer.drawMode = SpriteDrawMode.Simple;
            if (myData.IsRedNote && skin.RedBodySprite != null)
            {
                // RED 롱노트 몸통: 빛나는 양옆 테두리 + 사선 줄무늬 (색·투명도가 스프라이트에 들어 있음)
                //  Tiled: 가운데 줄무늬 타일이 길이·폭에 맞춰 반복됨 (늘어나서 일그러지지 않음)
                bodyRenderer.sprite = skin.RedBodySprite;
                bodyRenderer.drawMode = SpriteDrawMode.Tiled;
                bodyRenderer.tileMode = SpriteTileMode.Continuous;
                bodyRenderer.color = Color.white;
            }
            else
            {
                bodyRenderer.sprite = skin.PixelSprite;
                bodyRenderer.color = new Color(c.r, c.g, c.b, SimpleGameSkin.BodyAlpha);
            }
        }
    }

    private void RememberBaseColors()
    {
        headBaseColor = spriteRenderer.color;
        if (bodyRenderer != null) bodyBaseColor = bodyRenderer.color;
        appliedEndAlpha = 1f;
    }

    // GameManager.EndUiAlpha (곡 종료 0.75초 페이드 / FAILED) 를 따라 투명해짐
    private void ApplyEndAlpha()
    {
        float a = GameManager.Instance.EndUiAlpha;
        if (Mathf.Approximately(a, appliedEndAlpha)) return;
        appliedEndAlpha = a;
        spriteRenderer.color = new Color(headBaseColor.r, headBaseColor.g, headBaseColor.b, headBaseColor.a * a);
        if (bodyRenderer != null) bodyRenderer.color = new Color(bodyBaseColor.r, bodyBaseColor.g, bodyBaseColor.b, bodyBaseColor.a * a);
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
        // SIMPLE 스킨의 몸통은 반투명이라 놓쳤을 때도 투명도를 유지
        if (bodyRenderer) bodyRenderer.color = (SimpleGameSkin.Active != null && !myData.IsFreeKey)
            ? new Color(0.3f, 0.3f, 0.3f, SimpleGameSkin.BodyAlpha) : darkGray;
        RememberBaseColors();
    }
}