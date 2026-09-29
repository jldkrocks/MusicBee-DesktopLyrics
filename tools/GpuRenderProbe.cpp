// Standalone hardware-rendering feasibility probe; never loaded by MusicBee.
// Build from a Visual Studio x64 developer prompt:
// cl /nologo /EHsc /std:c++17 tools\GpuRenderProbe.cpp /Fe:bin\GpuRenderProbe.exe /link d2d1.lib dwrite.lib user32.lib ole32.lib
#include <windows.h>
#include <d2d1.h>
#include <dwrite.h>
#include <wrl/client.h>
#include <cmath>
#include <cstdio>
#include <initializer_list>
using Microsoft::WRL::ComPtr;
static void Check(HRESULT hr) { if (FAILED(hr)) throw hr; }
int main()
{
    HWND window = nullptr;
    HRESULT initialized = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    if (FAILED(initialized)) return 1;
    int result = 0;
    try {
        const wchar_t* name = L"DesktopLyricsGpuProbe";
        WNDCLASSW wc = {}; wc.lpfnWndProc = DefWindowProcW;
        wc.hInstance = GetModuleHandleW(nullptr); wc.lpszClassName = name;
        if (!RegisterClassW(&wc)) throw HRESULT_FROM_WIN32(GetLastError());
        window = CreateWindowW(name, name, WS_POPUP, 0, 0, 1, 1, nullptr, nullptr, wc.hInstance, nullptr);
        if (!window) throw HRESULT_FROM_WIN32(GetLastError());
        ComPtr<ID2D1Factory> factory;
        Check(D2D1CreateFactory(D2D1_FACTORY_TYPE_SINGLE_THREADED, factory.GetAddressOf()));
        ComPtr<IDWriteFactory> textFactory;
        Check(DWriteCreateFactory(DWRITE_FACTORY_TYPE_SHARED, __uuidof(IDWriteFactory),
            reinterpret_cast<IUnknown**>(textFactory.GetAddressOf())));
        ComPtr<IDWriteTextFormat> text;
        Check(textFactory->CreateTextFormat(L"Segoe UI", nullptr, DWRITE_FONT_WEIGHT_SEMI_BOLD,
            DWRITE_FONT_STYLE_NORMAL, DWRITE_FONT_STRETCH_NORMAL, 72, L"en-gb", &text));
        Check(text->SetTextAlignment(DWRITE_TEXT_ALIGNMENT_CENTER));
        ComPtr<ID2D1HwndRenderTarget> host;
        Check(factory->CreateHwndRenderTarget(D2D1::RenderTargetProperties(D2D1_RENDER_TARGET_TYPE_HARDWARE),
            D2D1::HwndRenderTargetProperties(window, D2D1::SizeU(1, 1)), &host));
        // Compatible targets inherit the hardware device. Draw off-screen so
        // testing cannot open a window over the user's work or alter MusicBee.
        for (unsigned width : { 1920u, 3840u }) {
            unsigned height = width * 9 / 16;
            ComPtr<ID2D1BitmapRenderTarget> target;
            Check(host->CreateCompatibleRenderTarget(D2D1::SizeF((float)width, (float)height), &target));
            ComPtr<ID2D1GradientStopCollection> stops;
            D2D1_GRADIENT_STOP colors[] = { {0, D2D1::ColorF(0.12f,0.2f,0.32f)}, {1, D2D1::ColorF(0.03f,0.04f,0.08f)} };
            Check(target->CreateGradientStopCollection(colors, 2, &stops));
            ComPtr<ID2D1LinearGradientBrush> gradient;
            Check(target->CreateLinearGradientBrush(D2D1::LinearGradientBrushProperties(
                D2D1::Point2F(0,0), D2D1::Point2F((float)width,(float)height)), stops.Get(), &gradient));
            ComPtr<ID2D1SolidColorBrush> brush;
            Check(target->CreateSolidColorBrush(D2D1::ColorF(1,1,1), &brush));
            for (int frame = 0; frame < 120; ++frame) {
                target->BeginDraw();
                target->FillRectangle(D2D1::RectF(0,0,(float)width,(float)height), gradient.Get());
                brush->SetColor(D2D1::ColorF(0.4f,0.7f,0.85f,0.5f));
                for (int bar = 0; bar < 48; ++bar) {
                    float x = width * bar / 48.f;
                    float top = height * (0.6f + 0.25f * std::sin(frame * .03f + bar));
                    target->FillRectangle(D2D1::RectF(x, top, x + width / 48.f - 3, (float)height), brush.Get());
                }
                brush->SetColor(D2D1::ColorF(1,1,1,0.9f));
                const wchar_t* lyric = L"GPU lyrics rendering prototype\nCurrent and upcoming lyrics";
                target->DrawText(lyric, (UINT32)wcslen(lyric), text.Get(),
                    D2D1::RectF(100,height * .3f,width - 100.f,height * .7f), brush.Get());
                Check(target->EndDraw());
            }
            Check(target->Flush());
            std::printf("Hardware Direct2D/DirectWrite: %ux%u, 120 off-screen frames passed.\n", width, height);
        }
        std::puts("Feasibility check only; no presentation/frame-rate claim. Plugin unchanged by this probe.");
    } catch (HRESULT hr) { std::printf("GPU probe failed: 0x%08lx\n", (unsigned long)hr); result = 1; }
    if (window) DestroyWindow(window);
    CoUninitialize(); return result;
}
