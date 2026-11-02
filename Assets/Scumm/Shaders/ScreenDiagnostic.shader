Shader "Scumm/ScreenDiagnostic"
{
    Properties
    {
        _IndexTex ("Room palette indices", 2D) = "black" {}
        _MaskTex ("Room foreground masks RGB", 2D) = "black" {}
        _MaskPlanes ("Available room mask planes", Float) = 0
        _ActClip0 ("Actor 0 mask", Float) = 0
        _ActClip1 ("Actor 1 mask", Float) = 0
        _ActClip2 ("Actor 2 mask", Float) = 0
        _ActClip3 ("Actor 3 mask", Float) = 0
        _ActClip4 ("Actor 4 mask", Float) = 0
        _ActClip5 ("Actor 5 mask", Float) = 0
        _ActClip6 ("Actor 6 mask", Float) = 0
        _ActClip7 ("Actor 7 mask", Float) = 0
        _PaletteTex ("256-color room palette", 2D) = "black" {}
        _RoomSize ("Room pixel dimensions", Vector) = (320,144,0,0)
        _CamX ("Room horizontal scroll, game pixels", Float) = 0
        _ShowRoom ("Show decoded background", Float) = 0
        _Cursor ("Cursor pixel XY, visible Z", Vector) = (0,0,0,0)
        _Click ("Last click pixel XY, visible Z", Vector) = (0,0,0,0)
        _ObjAtlas ("Object index atlas (R=index, A=coverage)", 2D) = "black" {}
        _ObjCount ("Visible object layers", Float) = 0
        _Obj0 ("Object 0 rect XYWH", Vector) = (0,0,0,0)
        _Obj1 ("Object 1 rect XYWH", Vector) = (0,0,0,0)
        _Obj2 ("Object 2 rect XYWH", Vector) = (0,0,0,0)
        _Obj3 ("Object 3 rect XYWH", Vector) = (0,0,0,0)
        _Obj4 ("Object 4 rect XYWH", Vector) = (0,0,0,0)
        _Obj5 ("Object 5 rect XYWH", Vector) = (0,0,0,0)
        _Obj6 ("Object 6 rect XYWH", Vector) = (0,0,0,0)
        _Obj7 ("Object 7 rect XYWH", Vector) = (0,0,0,0)
        _Obj8 ("Object 8 rect XYWH", Vector) = (0,0,0,0)
        _Obj9 ("Object 9 rect XYWH", Vector) = (0,0,0,0)
        _Obj10 ("Object 10 rect XYWH", Vector) = (0,0,0,0)
        _Obj11 ("Object 11 rect XYWH", Vector) = (0,0,0,0)
        _Obj12 ("Object 12 rect XYWH", Vector) = (0,0,0,0)
        _Obj13 ("Object 13 rect XYWH", Vector) = (0,0,0,0)
        _Obj14 ("Object 14 rect XYWH", Vector) = (0,0,0,0)
        _Obj15 ("Object 15 rect XYWH", Vector) = (0,0,0,0)
        _ObjUV0 ("Object 0 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV1 ("Object 1 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV2 ("Object 2 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV3 ("Object 3 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV4 ("Object 4 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV5 ("Object 5 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV6 ("Object 6 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV7 ("Object 7 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV8 ("Object 8 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV9 ("Object 9 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV10 ("Object 10 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV11 ("Object 11 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV12 ("Object 12 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV13 ("Object 13 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV14 ("Object 14 atlas UVs", Vector) = (0,0,0,0)
        _ObjUV15 ("Object 15 atlas UVs", Vector) = (0,0,0,0)
        _ActCount ("Visible actor layers", Float) = 0
        _ActAtlas0 ("Actor 0 atlas", 2D) = "black" {}
        _ActAtlas1 ("Actor 1 atlas", 2D) = "black" {}
        _ActAtlas2 ("Actor 2 atlas", 2D) = "black" {}
        _ActAtlas3 ("Actor 3 atlas", 2D) = "black" {}
        _ActAtlas4 ("Actor 4 atlas", 2D) = "black" {}
        _ActAtlas5 ("Actor 5 atlas", 2D) = "black" {}
        _ActAtlas6 ("Actor 6 atlas", 2D) = "black" {}
        _ActAtlas7 ("Actor 7 atlas", 2D) = "black" {}
        _Act0 ("Actor 0 rect XYWH", Vector) = (0,0,0,0)
        _Act1 ("Actor 1 rect XYWH", Vector) = (0,0,0,0)
        _Act2 ("Actor 2 rect XYWH", Vector) = (0,0,0,0)
        _Act3 ("Actor 3 rect XYWH", Vector) = (0,0,0,0)
        _Act4 ("Actor 4 rect XYWH", Vector) = (0,0,0,0)
        _Act5 ("Actor 5 rect XYWH", Vector) = (0,0,0,0)
        _Act6 ("Actor 6 rect XYWH", Vector) = (0,0,0,0)
        _Act7 ("Actor 7 rect XYWH", Vector) = (0,0,0,0)
        _ActUV0 ("Actor 0 atlas UVs", Vector) = (0,0,0,0)
        _ActUV1 ("Actor 1 atlas UVs", Vector) = (0,0,0,0)
        _ActUV2 ("Actor 2 atlas UVs", Vector) = (0,0,0,0)
        _ActUV3 ("Actor 3 atlas UVs", Vector) = (0,0,0,0)
        _ActUV4 ("Actor 4 atlas UVs", Vector) = (0,0,0,0)
        _ActUV5 ("Actor 5 atlas UVs", Vector) = (0,0,0,0)
        _ActUV6 ("Actor 6 atlas UVs", Vector) = (0,0,0,0)
        _ActUV7 ("Actor 7 atlas UVs", Vector) = (0,0,0,0)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        Pass
        {
            Cull Off
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"
            sampler2D _IndexTex, _PaletteTex, _ObjAtlas;
            sampler2D _MaskTex;
            float _MaskPlanes;
            float _CamX;
            float _ActClip0, _ActClip1, _ActClip2, _ActClip3, _ActClip4, _ActClip5, _ActClip6, _ActClip7;
            sampler2D _ActAtlas0, _ActAtlas1, _ActAtlas2, _ActAtlas3;
            sampler2D _ActAtlas4, _ActAtlas5, _ActAtlas6, _ActAtlas7;
            float4 _Cursor, _Click, _RoomSize;
            float _ShowRoom, _ObjCount, _ActCount;
            float4 _Obj0, _Obj1, _Obj2, _Obj3, _Obj4, _Obj5, _Obj6, _Obj7;
            float4 _Obj8, _Obj9, _Obj10, _Obj11, _Obj12, _Obj13, _Obj14, _Obj15;
            float4 _ObjUV0, _ObjUV1, _ObjUV2, _ObjUV3, _ObjUV4, _ObjUV5, _ObjUV6, _ObjUV7;
            float4 _ObjUV8, _ObjUV9, _ObjUV10, _ObjUV11, _ObjUV12, _ObjUV13, _ObjUV14, _ObjUV15;
            float4 _Act0, _Act1, _Act2, _Act3, _Act4, _Act5, _Act6, _Act7;
            float4 _ActUV0, _ActUV1, _ActUV2, _ActUV3, _ActUV4, _ActUV5, _ActUV6, _ActUV7;
            struct Input
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Output
            {
                float4 position : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Output vert(Input v)
            {
                Output o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(Output, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.position = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            float3 CompositeLayer(float3 color, float2 pixel, float4 rect, float4 uv, sampler2D atlas)
            {
                if (rect.z <= 0 || rect.w <= 0) return color;
                if (pixel.x < rect.x || pixel.x >= rect.x + rect.z
                    || pixel.y < rect.y || pixel.y >= rect.y + rect.w) return color;
                float2 f = float2((pixel.x - rect.x + .5) / rect.z,
                                   1 - (pixel.y - rect.y + .5) / rect.w);
                float2 auv = float2(lerp(uv.x, uv.z, f.x), lerp(uv.y, uv.w, f.y));
                float4 a = tex2D(atlas, auv);
                if (a.a < .5) return color;
                float index = floor(a.r * 255 + .5);
                return tex2D(_PaletteTex, float2((index + .5) / 256, .5)).rgb;
            }

            float3 CompositeLayerObj(float3 color, float2 pixel, float4 rect, float4 uv)
            {
                return CompositeLayer(color, pixel, rect, uv, _ObjAtlas);
            }

            float3 CompositeActor(float3 color, float2 pixel, float4 rect, float4 uv, sampler2D atlas, float plane)
            {
                plane = min(plane, _MaskPlanes);
                float worldX = pixel.x + _CamX;
                if (plane > .5 && worldX >= 0 && pixel.y >= 0 && worldX < _RoomSize.x && pixel.y < _RoomSize.y)
                {
                    float3 mask = tex2D(_MaskTex, float2((worldX+.5)/_RoomSize.x, 1-(pixel.y+.5)/_RoomSize.y)).rgb;
                    float covered = plane < 1.5 ? mask.r : (plane < 2.5 ? mask.g : mask.b);
                    if (covered > .5) return color;
                }
                return CompositeLayer(color, pixel, rect, uv, atlas);
            }

            float3 CompositeObjects(float3 color, float2 pixel)
            {
                if (_ObjCount > .5) color = CompositeLayerObj(color, pixel, _Obj0, _ObjUV0);
                if (_ObjCount > 1.5) color = CompositeLayerObj(color, pixel, _Obj1, _ObjUV1);
                if (_ObjCount > 2.5) color = CompositeLayerObj(color, pixel, _Obj2, _ObjUV2);
                if (_ObjCount > 3.5) color = CompositeLayerObj(color, pixel, _Obj3, _ObjUV3);
                if (_ObjCount > 4.5) color = CompositeLayerObj(color, pixel, _Obj4, _ObjUV4);
                if (_ObjCount > 5.5) color = CompositeLayerObj(color, pixel, _Obj5, _ObjUV5);
                if (_ObjCount > 6.5) color = CompositeLayerObj(color, pixel, _Obj6, _ObjUV6);
                if (_ObjCount > 7.5) color = CompositeLayerObj(color, pixel, _Obj7, _ObjUV7);
                if (_ObjCount > 8.5) color = CompositeLayerObj(color, pixel, _Obj8, _ObjUV8);
                if (_ObjCount > 9.5) color = CompositeLayerObj(color, pixel, _Obj9, _ObjUV9);
                if (_ObjCount > 10.5) color = CompositeLayerObj(color, pixel, _Obj10, _ObjUV10);
                if (_ObjCount > 11.5) color = CompositeLayerObj(color, pixel, _Obj11, _ObjUV11);
                if (_ObjCount > 12.5) color = CompositeLayerObj(color, pixel, _Obj12, _ObjUV12);
                if (_ObjCount > 13.5) color = CompositeLayerObj(color, pixel, _Obj13, _ObjUV13);
                if (_ObjCount > 14.5) color = CompositeLayerObj(color, pixel, _Obj14, _ObjUV14);
                if (_ObjCount > 15.5) color = CompositeLayerObj(color, pixel, _Obj15, _ObjUV15);
                if (_ActCount < .5) return color;
                if (_ActCount > .5) color = CompositeActor(color, pixel, _Act0, _ActUV0, _ActAtlas0, _ActClip0);
                if (_ActCount > 1.5) color = CompositeActor(color, pixel, _Act1, _ActUV1, _ActAtlas1, _ActClip1);
                if (_ActCount > 2.5) color = CompositeActor(color, pixel, _Act2, _ActUV2, _ActAtlas2, _ActClip2);
                if (_ActCount > 3.5) color = CompositeActor(color, pixel, _Act3, _ActUV3, _ActAtlas3, _ActClip3);
                if (_ActCount > 4.5) color = CompositeActor(color, pixel, _Act4, _ActUV4, _ActAtlas4, _ActClip4);
                if (_ActCount > 5.5) color = CompositeActor(color, pixel, _Act5, _ActUV5, _ActAtlas5, _ActClip5);
                if (_ActCount > 6.5) color = CompositeActor(color, pixel, _Act6, _ActUV6, _ActAtlas6, _ActClip6);
                if (_ActCount > 7.5) color = CompositeActor(color, pixel, _Act7, _ActUV7, _ActAtlas7, _ActClip7);
                return color;
            }

            fixed4 frag(Output i) : SV_Target
            {
                float2 pixel = floor(float2(i.uv.x, 1-i.uv.y) * float2(320,200));
                float3 color = 0;
                if (_ShowRoom > .5)
                {
                    float worldX = pixel.x + _CamX;
                    if (worldX < _RoomSize.x && pixel.y < _RoomSize.y)
                    {
                        float2 roomUV = float2((worldX+.5)/_RoomSize.x, 1-(pixel.y+.5)/_RoomSize.y);
                        float index = floor(tex2D(_IndexTex, roomUV).r * 255 + .5);
                        color = tex2D(_PaletteTex, float2((index+.5)/256, .5)).rgb;
                    }
                    color = CompositeObjects(color, pixel);
                }
                else if (_RoomSize.x > .5)
                {
                    float grid = max(1-step(1, fmod(pixel.x, 20)), 1-step(1, fmod(pixel.y, 20)));
                    color = lerp(float3(.025,.04,.065), float3(.09,.13,.19), grid);
                }
                if (_Click.z > .5 && max(abs(pixel.x-_Click.x),abs(pixel.y-_Click.y)) < 3)
                    color = float3(1,.45,.1);
                float2 cursorDistance = abs(pixel - _Cursor.xy - 0.5);
                if (_Cursor.z > .5 && (cursorDistance.x < 1.5 || cursorDistance.y < 1.5)
                    && max(cursorDistance.x, cursorDistance.y) <= 10)
                    color = float3(.2,1,.5);
                return fixed4(color,1);
            }
            ENDCG
        }
    }
}
