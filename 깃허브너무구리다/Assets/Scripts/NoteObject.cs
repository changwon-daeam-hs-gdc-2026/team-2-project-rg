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

    // 화살표 노트(7: >>> 밝은 파랑 / 8: <<< 밝은 노랑): 노트 위에 떠 있는 화살표 + 노트에서 나오는 빛
    private SpriteRenderer arrowRenderer, arrowGlowRenderer;
    private const float ArrowLift = 0.30f;      // 노트 중심에서 화살표까지 높이 (월드 유닛)
    private const float ArrowWidth = 0.62f;     // 화살표 폭
    private const float ArrowHeight = 0.24f;

    // 놓친 롱노트: 흘러가며 서서히 투명해짐
    private float missFade = -1f;               // < 0 = 사라지는 중 아님, 0 ~ 1 = 진행도
    private const float MissFadeSeconds = 0.45f;

    [Header("기본 에셋 설정")]
    public Sprite blueShort; public Sprite greenShort;
    public Sprite blueLongHead; public Sprite greenLongHead;
    public Sprite blueLongBody; public Sprite greenLongBody;
    public Sprite blueLongTail; public Sprite greenLongTail;

    private double myTargetDist;
    private double myNextTargetDist;
    private double myTailTime = 0.0;
    
    private Vector3 originalScale;

    private static Dictionary<string, Sprite> redNoteSprites = null;

    // 곡 종료 / FAILED 때 함께 투명해지기 위한 원래 색
    private Color headBaseColor = Color.white;
    private Color bodyBaseColor = Color.white;
    private float appliedEndAlpha = 1f;

    private static Sprite measureSprite;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale; 
        

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

        missFade = -1f;

        if (myData.IsRedNote)
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
            // [끊김 수정] 예전에는 마디선 노트가 나올 때마다 텍스처 · 스프라이트를 새로 만들었다
            //  (SIMPLE-DIVERSE 에서는 그때마다 원근 재질까지 새로 생김) → 한 번만 만들어 같이 씀
            if (measureSprite == null)
            {
                Texture2D tex = new Texture2D(1, 1);
                tex.SetPixel(0, 0, new Color(128, 128, 128, 0.2f));
                tex.Apply();
                measureSprite = Sprite.Create(tex, new Rect(0, 0, 1, 1), new Vector2(0.5f, 0.5f), 1f);
            }
            spriteRenderer.sprite = measureSprite;
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
            if (myData.Type == 1 || NoteTypes.IsArrow(myData.Type)) spriteRenderer.sprite = isBlue ? blueShort : greenShort;
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

        // 설정 > GAME UI 가 SIMPLE 이면 단색 둥근 노트로 바꾼다 (마디선은 기본 그대로)
        if (SimpleGameSkin.Active != null && myData.Type != 4) ApplySimpleStyle();

        SetupArrow();


        // 판정선 아래로는 그리지 않음 (GameManager 의 NoteClipMask)
        SpriteMaskInteraction clip = SpriteMaskInteraction.VisibleInsideMask;
        spriteRenderer.maskInteraction = clip;
        if (bodyRenderer != null) bodyRenderer.maskInteraction = clip;
        if (arrowRenderer != null) arrowRenderer.maskInteraction = clip;
        if (arrowGlowRenderer != null) arrowGlowRenderer.maskInteraction = clip;

        // SIMPLE-DIVERSE: 레인 노트는 원근 재질(판정선 아래 자르기 포함), 그 밖에는 원래 재질 (풀링 재사용 대비 매번 지정)
        if (baseMaterial == null) baseMaterial = spriteRenderer.sharedMaterial;
        if (bodyRenderer != null && baseBodyMaterial == null) baseBodyMaterial = bodyRenderer.sharedMaterial;
        DiversePlayfield pf = SimpleGameSkin.Active != null ? SimpleGameSkin.Active.Playfield : null;
        if (pf != null)
        {
            // 레인 접기(FOLD): 영역 안 노트는 접히지 않고 그대로 보이며 내려온다 (영역 밖 노트만 레인과 함께 접힘)
            bool fold = !GameManager.Instance.NoteInArea(myData);
            pf.Apply(spriteRenderer, true, fold);
            if (bodyRenderer != null) pf.Apply(bodyRenderer, true, fold);
            if (arrowRenderer != null) pf.Apply(arrowRenderer, true, fold);
            if (arrowGlowRenderer != null) pf.Apply(arrowGlowRenderer, true, fold);
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

                            if (bodyRenderer.drawMode == SpriteDrawMode.Tiled || bodyRenderer.sprite == NoteArt.LongBodySprite)
                            {
                                // SIMPLE 일반 롱노트 몸통(Sliced)도 같은 방식: 크기(size)를 머리 ~ 꼬리 구간에 맞춤
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
            if (NoteTypes.IsTapLike(myData.Type) || myData.Type == 3 || myData.Type == 13 || myData.Type == 4) 
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
        if (missFade >= 0f)
        {
            missFade += Time.deltaTime / MissFadeSeconds;
            if (missFade >= 1f) { GameManager.Instance.ReturnNoteToPool(this); return; }
        }
        ApplyEndAlpha();
        UpdateHeldBody();
        UpdateArrow();

        double currentTime = GameManager.Instance.currentPlayTime;
        // 자동 MISS 는 '판정 기준 시각'(JUDGE OFFSET 반영)으로 판단
        //  [버그 수정] 예전에는 화면 시각으로 판단해서, JUDGE OFFSET 을 바꾸면 늦게 친 쪽 판정 범위가 잘리거나(MISS 가 먼저 뜸)
        //   반대로 범위 밖인데도 노트가 남아 있었다.
        double judgeTime = GameManager.Instance.JudgeTime;

        if (judgeTime - myData.HitTime > GameManager.W_BAD && !myData.IsHit)
        {
            if (NoteTypes.IsTapLike(myData.Type)) 
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
        // 폭: RED · 넓은 일반 노트(영역 채보) 모두 Width 칸 (일반 1칸 노트는 그대로)
        float w = (Mathf.Max(1, myData.Width) - 1) * laneWidthGame + laneWidthGame * 0.95f;
        bool isTail = myData.Type == 3 || myData.Type == 13;
        Color c = SimpleGameSkin.NoteColor(myData);

        // [끊김 수정] 그림(sprite)을 먼저 바꾸고 나서 Sliced 로 바꾼다.
        //  예전에는 프리팹의 원래 노트 그림(Full Rect 가 아님)이 붙은 상태에서 Sliced 로 바꿔서, 노트가 나올 때마다
        //  'Sprite Tiling might not appear correctly…' 경고가 찍혔다(한 판에 수백 번). 에디터에서는 경고를 찍는 순간마다
        //  프레임이 끊기고, 그동안 밀린 시간만큼 노트가 진행 방향으로 순간이동한 것처럼 보였다.
        spriteRenderer.drawMode = SpriteDrawMode.Simple;
        float h = myData.IsRedNote
            ? (isTail ? SimpleGameSkin.RedTailHeight : SimpleGameSkin.RedNoteHeight)  // 빨간 노트는 조금 더 두껍게
            : (isTail ? SimpleGameSkin.TailHeight : SimpleGameSkin.NoteHeight);
        if (myData.IsRedNote && skin.RedNoteSprite != null)
        {
            // 고급 RED 노트: 색이 스프라이트에 들어 있으므로 흰색으로, 바깥 빛 여백만큼 크게 그림
            float pad = SimpleGameSkin.RedGlowPad * 2f;
            spriteRenderer.sprite = skin.RedNoteSprite;
            spriteRenderer.drawMode = SpriteDrawMode.Sliced;
            spriteRenderer.size = new Vector2(w + pad, h + pad);
            spriteRenderer.color = isTail ? new Color(1f, 1f, 1f, 0.9f) : Color.white;
        }
        else if (!isTail)
        {
            // 일반 · 화살표 노트: 입체 앞면(밝은 테두리 · 세로 그라데이션 · 광택 띠) + 바깥 은은한 빛 (빛 여백만큼 크게)
            float pad = NoteArt.NoteGlowPad * 2f;
            spriteRenderer.sprite = NoteArt.NoteFaceSprite;
            spriteRenderer.drawMode = SpriteDrawMode.Sliced;
            spriteRenderer.size = new Vector2(w + pad, h + pad);
            spriteRenderer.color = c;
        }
        else
        {
            // 롱노트 끝: 얇게 빛나는 막대 (위가 밝은 그라데이션 + 바깥 빛)
            float tpad = NoteArt.TailGlowPad * 2f;
            spriteRenderer.sprite = NoteArt.TailCapSprite;
            spriteRenderer.drawMode = SpriteDrawMode.Sliced;
            spriteRenderer.size = new Vector2(w + tpad, Mathf.Max(h, 0.075f) + tpad);
            spriteRenderer.color = new Color(c.r, c.g, c.b, 0.95f);
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
                // 일반 롱노트 몸통: 양옆이 빛나는 반투명 띠 (투명도가 스프라이트에 들어 있음)
                //  [버그 수정] Tiled 로 그리면 가운데 부분이 왼쪽부터 가로로도 반복돼서 가운데 하이라이트 선이 중심에서 어긋났다
                //   → Sliced: 좌우 테두리는 그대로, 가운데는 늘어나서 하이라이트가 항상 정가운데 (세로로는 무늬가 없어 늘려도 같음)
                bodyRenderer.sprite = NoteArt.LongBodySprite;
                bodyRenderer.drawMode = SpriteDrawMode.Sliced;
                bodyRenderer.color = new Color(c.r, c.g, c.b, 1f);
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
        if (missFade >= 0f) { float k = 1f - Mathf.Clamp01(missFade); a *= k * k * (3f - 2f * k); } // 놓친 롱노트: 서서히 투명
        if (Mathf.Approximately(a, appliedEndAlpha)) return;
        appliedEndAlpha = a;
        spriteRenderer.color = new Color(headBaseColor.r, headBaseColor.g, headBaseColor.b, headBaseColor.a * a);
        if (bodyRenderer != null) bodyRenderer.color = new Color(bodyBaseColor.r, bodyBaseColor.g, bodyBaseColor.b, bodyBaseColor.a * a);
    }

    // SIMPLE 롱노트를 누르고 있는 동안: 몸통이 흰빛으로 밝아지며 맥박처럼 빛남
    private void UpdateHeldBody()
    {
        if (bodyRenderer == null || SimpleGameSkin.Active == null || isMissed) return;
        if (bodyRenderer.sprite != NoteArt.LongBodySprite) return;
        if (!myData.IsHit || (myData.Type != 2 && myData.Type != 12)) return;
        float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * 14f);
        Color lit = Color.Lerp(bodyBaseColor, Color.white, 0.30f + 0.20f * pulse);
        bodyRenderer.color = new Color(lit.r, lit.g, lit.b, Mathf.Min(1f, bodyBaseColor.a * 1.15f) * appliedEndAlpha);
    }

    public void HideNote() {
        if (spriteRenderer) spriteRenderer.enabled = false;
        if (bodyRenderer) bodyRenderer.enabled = false;
        if (arrowRenderer) arrowRenderer.enabled = false;
        if (arrowGlowRenderer) arrowGlowRenderer.enabled = false;
    }

    // 놓친 롱노트(머리를 놓침 / 중간에 끊김): 색은 그대로 두고, 판정선에 붙어 있지 않고 레인을 따라 흘러가며
    //  MissFadeSeconds 동안 서서히 투명해진 뒤 사라진다. (예전: 회색으로 바뀐 채 끝까지 내려감)
    public void FadeOutMissed()
    {
        if (myData == null) return;
        myData.IsHit = true;
        isMissed = true;
        if (missFade < 0f) missFade = 0f;
    }

    // =====================================================================
    //  화살표 노트 (7: >>> 밝은 파랑, 8: <<< 밝은 노랑)
    // =====================================================================
    private void SetupArrow()
    {
        bool isArrow = NoteTypes.IsArrow(myData.Type);
        if (!isArrow)
        {
            if (arrowRenderer != null) arrowRenderer.gameObject.SetActive(false);
            if (arrowGlowRenderer != null) arrowGlowRenderer.gameObject.SetActive(false);
            return;
        }
        if (arrowRenderer == null)
        {
            GameObject a = new GameObject("Arrow");
            a.transform.SetParent(transform, false);
            arrowRenderer = a.AddComponent<SpriteRenderer>();
            GameObject g = new GameObject("ArrowGlow");
            g.transform.SetParent(transform, false);
            arrowGlowRenderer = g.AddComponent<SpriteRenderer>();
        }
        arrowRenderer.gameObject.SetActive(true);
        arrowGlowRenderer.gameObject.SetActive(true);
        arrowRenderer.sprite = NoteArt.ArrowSprite;
        arrowGlowRenderer.sprite = NoteArt.GlowSprite;
        arrowRenderer.enabled = arrowGlowRenderer.enabled = true;
        arrowRenderer.sortingOrder = spriteRenderer.sortingOrder + 1;          // 노트보다 위
        arrowGlowRenderer.sortingOrder = spriteRenderer.sortingOrder - 1;      // 노트 뒤에서 퍼지는 빛
        Color c = NoteArt.ArrowColor(myData.Type);
        arrowRenderer.color = c;
        arrowGlowRenderer.color = new Color(c.r, c.g, c.b, 0.55f);
        if (SimpleGameSkin.Active == null)
        {
            // 기본 스킨: 노트 스프라이트에 화살표 색을 살짝 입힘
            spriteRenderer.color = Color.Lerp(Color.white, c, 0.35f);
        }
        RememberBaseColors();
    }

    private void UpdateArrow()
    {
        if (arrowRenderer == null || !arrowRenderer.gameObject.activeSelf) return;
        bool show = spriteRenderer.enabled;
        arrowRenderer.enabled = arrowGlowRenderer.enabled = show;
        if (!show) return;

        // 부모(노트) 배율과 상관없이 월드 크기로 맞춤
        Vector3 ps = transform.lossyScale;
        float sx = Mathf.Max(0.0001f, Mathf.Abs(ps.x)), sy = Mathf.Max(0.0001f, Mathf.Abs(ps.y));
        float dir = myData.Type == NoteTypes.ArrowLeft ? -1f : 1f;
        float t = Time.time;
        float bob = Mathf.Sin(t * 7f) * 0.035f * dir;                    // 화살표 방향으로 살짝 밀렸다 돌아옴
        float yScale = SimpleGameSkin.Active != null ? SimpleGameSkin.Active.NoteScaleY : 1f;

        Vector2 asz = arrowRenderer.sprite.bounds.size;
        arrowRenderer.transform.position = transform.position + new Vector3(bob, ArrowLift * yScale, 0f);
        arrowRenderer.transform.localScale = new Vector3(dir * ArrowWidth / asz.x / sx, ArrowHeight * yScale / asz.y / sy, 1f);

        Vector2 gsz = arrowGlowRenderer.sprite.bounds.size;
        float pulse = 0.8f + 0.2f * Mathf.Sin(t * 5f);
        arrowGlowRenderer.transform.position = transform.position + new Vector3(0f, ArrowLift * 0.45f * yScale, 0f);
        arrowGlowRenderer.transform.localScale = new Vector3(0.95f * pulse / gsz.x / sx, 0.75f * yScale * pulse / gsz.y / sy, 1f);

        // 곡 종료 페이드 · 놓친 노트 페이드를 같이 따라감
        float a = spriteRenderer.color.a / Mathf.Max(0.001f, headBaseColor.a);
        if (isMissed && missFade < 0f) a *= 0.35f;                     // 놓친 화살표 노트는 흐리게
        Color c = NoteArt.ArrowColor(myData.Type);
        arrowRenderer.color = new Color(c.r, c.g, c.b, a);
        arrowGlowRenderer.color = new Color(c.r, c.g, c.b, 0.55f * a * pulse);
    }

    public void DimNote() {
        isMissed = true;
        myData.IsHit = true; 
        Color darkGray = new Color(0.3f, 0.3f, 0.3f, 1f); 
        if (spriteRenderer) spriteRenderer.color = darkGray;
        // SIMPLE 스킨의 몸통은 투명도가 스프라이트에 들어 있으므로 색만 어둡게 (조금 더 흐리게)
        if (bodyRenderer) bodyRenderer.color = (SimpleGameSkin.Active != null)
            ? new Color(0.3f, 0.3f, 0.3f, 0.8f) : darkGray;
        RememberBaseColors();
    }
}