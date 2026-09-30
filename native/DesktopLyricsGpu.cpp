// Narrow rendering-only ABI. No playback state, timers, song data or callbacks.
#include <windows.h>
#include <d2d1.h>
#include <wrl/client.h>
#include <new>
#include <cmath>
#include <cstring>
using Microsoft::WRL::ComPtr;
struct Scene {
    UINT colors[6]; // left, right, bar top, bar bottom, border, accent (ARGB)
    float bars[48];
    int spectrum;
};
struct TextCommand { int slot; D2D1_RECT_F destination, clip; float opacity; int nearest; };
struct DancerCommand { int slot; D2D1_RECT_F destination, clip; };
struct OutlineCommand { int slot; D2D1_RECT_F clip; float x,y,scale,stroke,opacity; UINT color1,color2,border; int gradient; };
struct Outline {
    ComPtr<ID2D1PathGeometry> path;
    ComPtr<ID2D1LinearGradientBrush> gradient;
    D2D1_RECT_F bounds = {};
    UINT points=0, color1=0, color2=0;
    int gradientMode=-1;
};
struct Renderer {
    ComPtr<ID2D1Factory> factory;
    ComPtr<ID2D1HwndRenderTarget> target;
    ComPtr<ID2D1Bitmap> foreground;
    ComPtr<ID2D1LinearGradientBrush> background, spectrum;
    ComPtr<ID2D1RadialGradientBrush> glow1, glow2;
    UINT colors[6] = {};
    UINT width = 0, height = 0;
    bool brushes = false;
    ComPtr<ID2D1Bitmap> text[24];
    UINT textBytes[24] = {};
    TextCommand commands[8] = {};
    int commandCount = 0;
    D2D1_RECT_F panel = {};
    ComPtr<ID2D1SolidColorBrush> panelBrush;
    ComPtr<ID2D1Bitmap> dancers[8];
    UINT dancerBytes[8] = {};
    DancerCommand dancerCommands[2] = {};
    int dancerCount = 0;
    Outline outlines[24];
    OutlineCommand outlineCommands[8] = {};
    int outlineCount=0;
    ComPtr<ID2D1SolidColorBrush> ink;
    ComPtr<ID2D1StrokeStyle> roundStroke;
    ComPtr<ID2D1Layer> textLayer;
};
static D2D1_COLOR_F Color(UINT argb, float alpha = 1) {
    return D2D1::ColorF((argb >> 16 & 255) / 255.f, (argb >> 8 & 255) / 255.f, (argb & 255) / 255.f, alpha);
}
extern "C" HRESULT __cdecl DL_Outline(Renderer* r,int slot,const D2D1_POINT_2F* points,
    const unsigned char* types,int count,int fill,const D2D1_RECT_F* bounds) noexcept {
    if(!r || slot<0 || slot>=24 || count<0 || count>65536)return E_INVALIDARG;
    auto& o=r->outlines[slot];
    if(!count){o=Outline();return S_OK;}
    if(!points || !types || !bounds || fill<0 || fill>1)return E_INVALIDARG;
    UINT total=count;for(int i=0;i<24;i++)if(i!=slot)total+=r->outlines[i].points;
    if(total>262144)return E_OUTOFMEMORY;
    if(!std::isfinite(bounds->left)||!std::isfinite(bounds->top)||!std::isfinite(bounds->right)||
       !std::isfinite(bounds->bottom)||bounds->right<bounds->left||bounds->bottom<bounds->top)return E_INVALIDARG;
    ComPtr<ID2D1PathGeometry> path;HRESULT hr=r->factory->CreatePathGeometry(&path);if(FAILED(hr))return hr;
    ComPtr<ID2D1GeometrySink> sink;hr=path->Open(&sink);if(FAILED(hr))return hr;
    sink->SetFillMode(fill?D2D1_FILL_MODE_WINDING:D2D1_FILL_MODE_ALTERNATE);
    for(int i=0;i<count;i++)if(!std::isfinite(points[i].x)||!std::isfinite(points[i].y))return E_INVALIDARG;
    bool open=false;
    for(int i=0;i<count;i++){
        int kind=types[i]&7;
        if(kind==0){if(open)sink->EndFigure(D2D1_FIGURE_END_OPEN);sink->BeginFigure(points[i],D2D1_FIGURE_BEGIN_FILLED);open=true;}
        else if(kind==1 && open)sink->AddLine(points[i]);
        else if(kind==3 && open && i+2<count && (types[i+1]&7)==3 && (types[i+2]&7)==3 && !(types[i]&128) && !(types[i+1]&128)){
            sink->AddBezier(D2D1::BezierSegment(points[i],points[i+1],points[i+2]));i+=2;
        }else return E_INVALIDARG;
        if(types[i]&128){sink->EndFigure(D2D1_FIGURE_END_CLOSED);open=false;}
    }
    if(open)sink->EndFigure(D2D1_FIGURE_END_OPEN);
    hr=sink->Close();if(FAILED(hr))return hr;
    o=Outline();o.path=path;o.bounds=*bounds;o.points=count;return S_OK;
}
extern "C" HRESULT __cdecl DL_OutlineLyrics(Renderer* r,const OutlineCommand* commands,int count) noexcept {
    if(!r || count<0 || count>8 || (count&&!commands))return E_INVALIDARG;
    if(count && !r->ink){HRESULT hr=r->target->CreateSolidColorBrush(D2D1::ColorF(0,0,0),&r->ink);if(FAILED(hr))return hr;}
    if(count && !r->roundStroke){auto p=D2D1::StrokeStyleProperties();p.lineJoin=D2D1_LINE_JOIN_ROUND;
        HRESULT hr=r->factory->CreateStrokeStyle(p,nullptr,0,&r->roundStroke);if(FAILED(hr))return hr;}
    if(count && !r->textLayer){HRESULT hr=r->target->CreateLayer(nullptr,&r->textLayer);if(FAILED(hr))return hr;}
    for(int i=0;i<count;i++){
        const auto& c=commands[i];
        if(c.slot<0||c.slot>=24||!r->outlines[c.slot].path||!std::isfinite(c.x)||!std::isfinite(c.y)||
           !std::isfinite(c.scale)||c.scale<=0||c.scale>8||!std::isfinite(c.opacity)||c.opacity<0||c.opacity>1||
           !std::isfinite(c.stroke)||c.stroke<0||c.stroke>10||c.gradient<0||c.gradient>2)return E_INVALIDARG;
        auto& o=r->outlines[c.slot];
        if(c.gradient && (!o.gradient || o.color1!=c.color1 || o.color2!=c.color2 || o.gradientMode!=c.gradient)){
            D2D1_GRADIENT_STOP stops[3]={{0,Color(c.color1)},{c.gradient==2?.5f:1.f,Color(c.color2)},{1,Color(c.color1)}};
            ComPtr<ID2D1GradientStopCollection> collection;
            HRESULT hr=r->target->CreateGradientStopCollection(stops,c.gradient==2?3:2,D2D1_GAMMA_2_2,D2D1_EXTEND_MODE_CLAMP,&collection);
            if(FAILED(hr))return hr;
            o.gradient.Reset();hr=r->target->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
                D2D1::Point2F(0,o.bounds.top),D2D1::Point2F(0,o.bounds.bottom)),collection.Get(),&o.gradient);
            if(FAILED(hr))return hr;o.color1=c.color1;o.color2=c.color2;o.gradientMode=c.gradient;
        }
    }
    r->outlineCount=count;if(count)memcpy(r->outlineCommands,commands,sizeof(OutlineCommand)*count);return S_OK;
}
static UINT Mix(UINT a, UINT b, float t) {
    UINT result = 0xff000000;
    for (int shift = 0; shift <= 16; shift += 8) {
        int x = (a >> shift) & 255, y = (b >> shift) & 255;
        result |= static_cast<UINT>(x + (y - x) * t) << shift;
    }
    return result;
}
static HRESULT Brushes(Renderer& r, const Scene& s) {
    if (r.brushes && !memcmp(r.colors, s.colors, sizeof r.colors)) return S_OK;
    r.brushes = false;
    r.background.Reset(); r.spectrum.Reset(); r.glow1.Reset(); r.glow2.Reset();
    ComPtr<ID2D1GradientStopCollection> stops;
    D2D1_GRADIENT_STOP bg[] = {{0,Color(s.colors[0])},{.32f,Color(Mix(s.colors[0],s.colors[5],.32f))},
        {.72f,Color(Mix(s.colors[1],s.colors[3],.17f))},{1,Color(s.colors[1])}};
    HRESULT hr = r.target->CreateGradientStopCollection(bg, 4, D2D1_GAMMA_2_2, D2D1_EXTEND_MODE_CLAMP, &stops);
    if (FAILED(hr)) return hr;
    hr = r.target->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
        D2D1::Point2F(0,0), D2D1::Point2F((float)r.width,0)), stops.Get(), &r.background);
    if (FAILED(hr)) return hr;
    stops.Reset();
    D2D1_GRADIENT_STOP bar[] = {{0,Color(s.colors[2],124/255.f)},{1,Color(s.colors[3],165/255.f)}};
    hr = r.target->CreateGradientStopCollection(bar,2,&stops);
    if (FAILED(hr)) return hr;
    hr = r.target->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
        D2D1::Point2F(0,16),D2D1::Point2F(0,(float)r.height-10)),stops.Get(),&r.spectrum);
    if (FAILED(hr)) return hr;
    for (int i=0;i<2;i++) {
        stops.Reset();
        UINT color=s.colors[i==0?5:3];
        D2D1_GRADIENT_STOP glow[]={{0,Color(color,(i==0?34:25)/255.f)},{1,Color(color,0)}};
        hr=r.target->CreateGradientStopCollection(glow,2,&stops);
        if(FAILED(hr)) return hr;
        auto props=D2D1::RadialGradientBrushProperties(D2D1::Point2F(
            r.width*(i==0?.295f:.84f),r.height*(i==0?.105f:.8f)),D2D1::Point2F(0,0),
            r.width*(i==0?.415f:.32f),r.height*(i==0?.585f:.48f));
        hr=r.target->CreateRadialGradientBrush(props,stops.Get(),i==0?&r.glow1:&r.glow2);
        if(FAILED(hr)) return hr;
    }
    memcpy(r.colors,s.colors,sizeof r.colors); r.brushes=true; return S_OK;
}
extern "C" HRESULT __cdecl DL_Create(HWND window, UINT width, UINT height, int diagnosticReadback, Renderer** output) noexcept {
    if (!output) return E_POINTER;
    *output=nullptr;
    if (!IsWindow(window) || !width || !height || width>8192 || height>8192) return E_INVALIDARG;
    auto r=new(std::nothrow) Renderer(); if(!r) return E_OUTOFMEMORY;
    HRESULT hr=D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED,r->factory.GetAddressOf());
    if(SUCCEEDED(hr)) hr=r->factory->CreateHwndRenderTarget(
        D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_HARDWARE,
            D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_IGNORE),96,96,
            diagnosticReadback ? D2D1_RENDER_TARGET_USAGE_GDI_COMPATIBLE : D2D1_RENDER_TARGET_USAGE_NONE),
        D2D1::HwndRenderTargetProperties(window,D2D1::SizeU(width,height),D2D1_PRESENT_OPTIONS_IMMEDIATELY),&r->target);
    if(FAILED(hr)) {delete r;return hr;}
    r->width=width;r->height=height;*output=r;return S_OK;
}
extern "C" void __cdecl DL_Destroy(Renderer* r) noexcept {delete r;}
extern "C" HRESULT __cdecl DL_Resize(Renderer* r, UINT width, UINT height) noexcept {
    if(!r || !width || !height || width>8192 || height>8192) return E_INVALIDARG;
    if(r->width==width && r->height==height) return S_OK;
    r->foreground.Reset(); r->brushes=false;
    HRESULT hr=r->target->Resize(D2D1::SizeU(width,height));
    if(SUCCEEDED(hr)) {r->width=width;r->height=height;} return hr;
}
extern "C" HRESULT __cdecl DL_Upload(Renderer* r, const void* pixels, UINT stride) noexcept {
    if(!r || !pixels || stride<r->width*4) return E_INVALIDARG;
    if(!r->foreground) return r->target->CreateBitmap(D2D1::SizeU(r->width,r->height),pixels,stride,
        D2D1::BitmapProperties(D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96),&r->foreground);
    return r->foreground->CopyFromMemory(nullptr,pixels,stride);
}
extern "C" HRESULT __cdecl DL_Text(Renderer* r, int slot, UINT width, UINT height, const void* pixels, UINT stride) noexcept {
    if(!r || slot<0 || slot>=24) return E_INVALIDARG;
    r->text[slot].Reset();r->textBytes[slot]=0;
    if(!pixels) return S_OK; // Explicit bounded-cache eviction.
    if(!width || !height || width>8192 || height>8192 || stride<width*4) return E_INVALIDARG;
    UINT bytes=width*height*4,total=bytes;
    for(auto b:r->textBytes) total+=b;
    if(total>24*1024*1024) return E_OUTOFMEMORY;
    HRESULT hr=r->target->CreateBitmap(D2D1::SizeU(width,height),pixels,stride,
        D2D1::BitmapProperties(D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96),&r->text[slot]);
    if(SUCCEEDED(hr))r->textBytes[slot]=bytes;
    return hr;
}
extern "C" HRESULT __cdecl DL_Lyrics(Renderer* r, const D2D1_RECT_F* panel, const TextCommand* commands, int count) noexcept {
    if(!r || !panel || count<0 || count>8 || (count && !commands))return E_INVALIDARG;
    for(int i=0;i<count;i++) if(commands[i].slot<0 || commands[i].slot>=24 || !r->text[commands[i].slot])return E_INVALIDARG;
    r->panel=*panel;r->commandCount=count;
    if(count)memcpy(r->commands,commands,sizeof(TextCommand)*count);
    return S_OK;
}
extern "C" HRESULT __cdecl DL_Draw(Renderer* r, const Scene* s, HDC diagnosticOutput) noexcept {
    if(!r || !s) return E_INVALIDARG;
    HRESULT hr=Brushes(*r,*s); if(FAILED(hr)) return hr;
    if(!r->panelBrush) {
        hr=r->target->CreateSolidColorBrush(D2D1::ColorF(0.f,0.f,0.f,0.f),&r->panelBrush);
        if(FAILED(hr))return hr;
    }
    r->target->BeginDraw();
    auto bounds=D2D1::RectF(0,0,(float)r->width,(float)r->height);
    r->target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_ALIASED);
    r->target->FillRectangle(bounds,r->background.Get());
    r->target->FillRectangle(bounds,r->glow1.Get());
    r->target->FillRectangle(bounds,r->glow2.Get());
    if(s->spectrum) {
        float spacing=(r->width-46.f)/48, barWidth=(spacing-3)>2?spacing-3:2, floor=r->height-10.f;
        for(int i=0;i<48;i++) {
            float level=s->bars[i];if(!std::isfinite(level)) level=0;
            level=level<0?0:level>1?1:level;
            float h=level*(r->height-28.f);if(h<2)h=2;
            r->target->FillRectangle(D2D1::RectF(std::round(23+i*spacing),floor-std::round(h),
                std::round(23+i*spacing)+std::round(barWidth),floor),r->spectrum.Get());
        }
    }
    if(r->panel.right>r->panel.left && r->panel.bottom>r->panel.top) {
        r->target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
        auto card=D2D1::RoundedRect(r->panel,14,14);
        r->panelBrush->SetColor(D2D1::ColorF(10/255.f,13/255.f,27/255.f,128/255.f));
        r->target->FillRoundedRectangle(card,r->panelBrush.Get());
        r->panelBrush->SetColor(Color(s->colors[4],56/255.f));
        r->target->DrawRoundedRectangle(card,r->panelBrush.Get(),1);
    }
    for(int i=0;i<r->commandCount;i++) {
        const auto& c=r->commands[i];
        r->target->PushAxisAlignedClip(c.clip,D2D1_ANTIALIAS_MODE_ALIASED);
        r->target->DrawBitmap(r->text[c.slot].Get(),c.destination,c.opacity,
            c.nearest?D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR:D2D1_BITMAP_INTERPOLATION_MODE_LINEAR);
        r->target->PopAxisAlignedClip();
    }
    r->target->SetAntialiasMode(D2D1_ANTIALIAS_MODE_PER_PRIMITIVE);
    for(int i=0;i<r->outlineCount;i++) {
        const auto& c=r->outlineCommands[i];const auto& o=r->outlines[c.slot];
        auto transform=D2D1::Matrix3x2F::Translation(-o.bounds.left,-o.bounds.top)*
            D2D1::Matrix3x2F::Scale(c.scale,c.scale)*D2D1::Matrix3x2F::Translation(c.x,c.y);
        r->target->PushAxisAlignedClip(c.clip,D2D1_ANTIALIAS_MODE_ALIASED);
        // Opacity applies to the whole shadow/outline/fill group, like the old
        // premultiplied bitmap. Per-brush opacity would change overlapping edges.
        if(c.opacity<1)r->target->PushLayer(D2D1::LayerParameters(c.clip,nullptr,D2D1_ANTIALIAS_MODE_PER_PRIMITIVE,
            D2D1::Matrix3x2F::Identity(),c.opacity),r->textLayer.Get());
        r->target->SetTransform(transform*D2D1::Matrix3x2F::Translation(c.scale,2*c.scale));
        r->ink->SetColor(D2D1::ColorF(0,0,0,170/255.f));r->target->FillGeometry(o.path.Get(),r->ink.Get());
        r->target->SetTransform(transform);
        r->ink->SetColor(Color(c.border,((c.border>>24)&255)/255.f));
        r->target->DrawGeometry(o.path.Get(),r->ink.Get(),c.stroke,r->roundStroke.Get());
        r->ink->SetColor(Color(c.color1));
        r->target->FillGeometry(o.path.Get(),c.gradient?static_cast<ID2D1Brush*>(o.gradient.Get()):r->ink.Get());
        r->target->SetTransform(D2D1::Matrix3x2F::Identity());
        if(c.opacity<1)r->target->PopLayer();
        r->target->PopAxisAlignedClip();
    }
    // UI remains above the lyric layer, preserving queue/menu overlap order.
    if(r->foreground) r->target->DrawBitmap(r->foreground.Get(),bounds,1,D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
    // Same z-order as the former owned windows. Clip to each dancer's bounds,
    // preserving the transparent layered window's edge behavior.
    for(int i=0;i<r->dancerCount;i++) {
        const auto& c=r->dancerCommands[i];
        r->target->PushAxisAlignedClip(c.clip,D2D1_ANTIALIAS_MODE_ALIASED);
        r->target->DrawBitmap(r->dancers[c.slot].Get(),c.destination,1,D2D1_BITMAP_INTERPOLATION_MODE_NEAREST_NEIGHBOR);
        r->target->PopAxisAlignedClip();
    }
    // Test-only readback for pixel comparisons. Production always passes null
    // and does not create a GDI-compatible target or transfer pixels to the CPU.
    if(diagnosticOutput) {
        ComPtr<ID2D1GdiInteropRenderTarget> interop;
        hr=r->target.As(&interop);
        if(SUCCEEDED(hr)) {
            HDC dc=nullptr;hr=interop->GetDC(D2D1_DC_INITIALIZE_MODE_COPY,&dc);
            if(SUCCEEDED(hr)) {
                if(!BitBlt(diagnosticOutput,0,0,r->width,r->height,dc,0,0,SRCCOPY)) hr=E_FAIL;
                HRESULT released=interop->ReleaseDC(nullptr);if(SUCCEEDED(hr))hr=released;
            }
        }
    }
    if(FAILED(hr)) {r->target->EndDraw();return hr;}
    return r->target->EndDraw(); // Includes submission/present; any device loss returns to GDI.
}

extern "C" HRESULT __cdecl DL_DancerTexture(Renderer* r,int slot,UINT width,UINT height,const void* pixels,UINT stride) noexcept {
    if(!r || slot<0 || slot>=8)return E_INVALIDARG;
    r->dancers[slot].Reset();r->dancerBytes[slot]=0;
    if(!pixels)return S_OK;
    if(!width || !height || width>8192 || height>8192 || stride<width*4)return E_INVALIDARG;
    UINT bytes=width*height*4;
    if(bytes>8*1024*1024)return E_OUTOFMEMORY; // eight slots, at most 64 MiB
    HRESULT hr=r->target->CreateBitmap(D2D1::SizeU(width,height),pixels,stride,
        D2D1::BitmapProperties(D2D1::PixelFormat(DXGI_FORMAT_B8G8R8A8_UNORM,D2D1_ALPHA_MODE_PREMULTIPLIED),96,96),&r->dancers[slot]);
    if(SUCCEEDED(hr))r->dancerBytes[slot]=bytes;
    return hr;
}
extern "C" HRESULT __cdecl DL_Dancers(Renderer* r,const DancerCommand* commands,int count) noexcept {
    if(!r || count<0 || count>2 || (count && !commands))return E_INVALIDARG;
    for(int i=0;i<count;i++) {
        const auto& c=commands[i];
        if(c.slot<0 || c.slot>=8 || !r->dancers[c.slot] ||
            !std::isfinite(c.destination.left) || !std::isfinite(c.destination.top) ||
            !std::isfinite(c.destination.right) || !std::isfinite(c.destination.bottom))return E_INVALIDARG;
    }
    r->dancerCount=count;
    if(count)memcpy(r->dancerCommands,commands,sizeof(DancerCommand)*count);
    return S_OK;
}
