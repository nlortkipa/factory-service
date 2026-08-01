//go:build windows

// Factory Windows Agent currently proves one part of the production agent:
// it reacts to the logged-in Windows session being locked and unlocked.
package main

import (
	"fmt"
	"os"
	"path/filepath"
	"syscall"
	"time"
	"unsafe"
)

const (
	appName = "Factory Service Windows Agent"

	wmDestroy          = 0x0002
	wmWtsSessionChange = 0x02B1

	wtsSessionLock   = 0x7
	wtsSessionUnlock = 0x8

	mbIconInformation = 0x00000040
)

var (
	user32                           = syscall.NewLazyDLL("user32.dll")
	kernel32                         = syscall.NewLazyDLL("kernel32.dll")
	wtsapi32                         = syscall.NewLazyDLL("wtsapi32.dll")
	registerClassExW                 = user32.NewProc("RegisterClassExW")
	createWindowExW                  = user32.NewProc("CreateWindowExW")
	defWindowProcW                   = user32.NewProc("DefWindowProcW")
	getMessageW                      = user32.NewProc("GetMessageW")
	translateMessage                 = user32.NewProc("TranslateMessage")
	dispatchMessageW                 = user32.NewProc("DispatchMessageW")
	postQuitMessage                  = user32.NewProc("PostQuitMessage")
	messageBoxW                      = user32.NewProc("MessageBoxW")
	getModuleHandleW                 = kernel32.NewProc("GetModuleHandleW")
	wtsRegisterSessionNotification   = wtsapi32.NewProc("WTSRegisterSessionNotification")
	wtsUnregisterSessionNotification = wtsapi32.NewProc("WTSUnRegisterSessionNotification")
)

type wndClassEx struct {
	cbSize     uint32
	style      uint32
	wndProc    uintptr
	clsExtra   int32
	wndExtra   int32
	instance   uintptr
	icon       uintptr
	cursor     uintptr
	background uintptr
	menuName   *uint16
	className  *uint16
	iconSmall  uintptr
}

type point struct {
	x int32
	y int32
}

type message struct {
	hwnd    uintptr
	message uint32
	wParam  uintptr
	lParam  uintptr
	time    uint32
	pt      point
}

func main() {
	if err := appendEvent("agent started; waiting for Windows lock/unlock events"); err != nil {
		fmt.Fprintln(os.Stderr, "log error:", err)
	}

	// fmt.Println("Factory Service Windows Agent is running.")
	// fmt.Println("Lock the PC with Win + L, sign back in, then look for the welcome dialog.")
	// fmt.Println("Press Ctrl + C to stop.")

	if err := listenForSessionChanges(); err != nil {
		fmt.Fprintln(os.Stderr, "agent stopped:", err)
		os.Exit(1)
	}
}

func listenForSessionChanges() error {
	instance, _, err := getModuleHandleW.Call(0)
	if instance == 0 {
		return fmt.Errorf("get module handle: %w", err)
	}

	className := syscall.StringToUTF16Ptr("FactoryServiceSessionListener")
	class := wndClassEx{
		cbSize:    uint32(unsafe.Sizeof(wndClassEx{})),
		wndProc:   syscall.NewCallback(windowProc),
		instance:  instance,
		className: className,
	}
	if atom, _, err := registerClassExW.Call(uintptr(unsafe.Pointer(&class))); atom == 0 {
		return fmt.Errorf("register window class: %w", err)
	}

	// The window deliberately has no visible style. It only receives Windows
	// session events; dialogs are shown separately when an event matters.
	hwnd, _, err := createWindowExW.Call(
		0, uintptr(unsafe.Pointer(className)), uintptr(unsafe.Pointer(syscall.StringToUTF16Ptr(appName))),
		0, 0, 0, 0, 0, 0, 0, instance, 0,
	)
	if hwnd == 0 {
		return fmt.Errorf("create listener window: %w", err)
	}

	if ok, _, err := wtsRegisterSessionNotification.Call(hwnd, 0); ok == 0 {
		return fmt.Errorf("register session notifications: %w", err)
	}
	defer wtsUnregisterSessionNotification.Call(hwnd)

	var msg message
	for {
		result, _, err := getMessageW.Call(uintptr(unsafe.Pointer(&msg)), 0, 0, 0)
		switch result {
		case ^uintptr(0):
			return fmt.Errorf("read Windows message: %w", err)
		case 0:
			return nil
		default:
			translateMessage.Call(uintptr(unsafe.Pointer(&msg)))
			dispatchMessageW.Call(uintptr(unsafe.Pointer(&msg)))
		}
	}
}

func windowProc(hwnd uintptr, msg uint32, wParam, lParam uintptr) uintptr {
	switch msg {
	case wmWtsSessionChange:
		switch wParam {
		case wtsSessionLock:
			_ = appendEvent("Windows session locked")
		case wtsSessionUnlock:
			_ = appendEvent("Windows session unlocked")
			showWelcomeBack()
		}
		return 0
	case wmDestroy:
		postQuitMessage.Call(0)
		return 0
	default:
		result, _, _ := defWindowProcW.Call(hwnd, uintptr(msg), wParam, lParam)
		return result
	}
}

func showWelcomeBack() {
	message := "Welcome back. The Factory Service Windows Agent detected that this Windows session was unlocked."
	messageBoxW.Call(
		0,
		uintptr(unsafe.Pointer(syscall.StringToUTF16Ptr(message))),
		uintptr(unsafe.Pointer(syscall.StringToUTF16Ptr(appName+": Welcome back"))),
		mbIconInformation,
	)
}

func appendEvent(event string) error {
	localAppData := os.Getenv("LOCALAPPDATA")
	if localAppData == "" {
		return fmt.Errorf("LOCALAPPDATA is not available")
	}

	logDir := filepath.Join(localAppData, "FactoryService", "logs")
	if err := os.MkdirAll(logDir, 0o755); err != nil {
		return fmt.Errorf("create log directory: %w", err)
	}

	file, err := os.OpenFile(filepath.Join(logDir, "agent.log"), os.O_APPEND|os.O_CREATE|os.O_WRONLY, 0o644)
	if err != nil {
		return fmt.Errorf("open log: %w", err)
	}
	defer file.Close()

	_, err = fmt.Fprintf(file, "%s %s\r\n", time.Now().Format(time.RFC3339), event)
	return err
}
