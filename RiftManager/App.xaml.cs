using System;
using System.Windows;
using System.Windows.Threading;
using RiftManager.Services;

namespace RiftManager
{
    public partial class App : Application
    {
        private LogService? _globalLogService; // Usar un campo para el LogService

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Inicializar el LogService aquí para que esté disponible globalmente
            _globalLogService = new LogService();
            _globalLogService.Log("Application started.");

            // Manejar excepciones no controladas en todos los hilos de la aplicación
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // Manejar excepciones no controladas en el hilo de la UI
            this.DispatcherUnhandledException += App_DispatcherUnhandledException;
        }

        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            _globalLogService?.LogError($"[CRITICAL UNHANDLED EXCEPTION - AppDomain] Message: {ex.Message}\nStackTrace: {ex.StackTrace}");
            // Opcional: Mostrar un MessageBox al usuario
            MessageBox.Show($"An unhandled application error occurred: {ex.Message}\nSee application.log for details.", "Application Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1); // Terminar la aplicación
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Exception ex = e.Exception;
            _globalLogService?.LogError($"[CRITICAL UNHANDLED EXCEPTION - Dispatcher] Message: {ex.Message}\nStackTrace: {ex.StackTrace}");
            // Marcar la excepción como manejada para evitar que la aplicación se cierre inmediatamente
            // Esto permite que el log se escriba antes de que la aplicación se cierre (si se cierra)
            e.Handled = true; 
            // Opcional: Mostrar un MessageBox al usuario
            MessageBox.Show($"An unhandled UI error occurred: {ex.Message}\nSee application.log for details.", "UI Error", MessageBoxButton.OK, MessageBoxImage.Error);
            Environment.Exit(1); // Terminar la aplicación
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _globalLogService?.Log("Application exiting.");
            _globalLogService?.Dispose(); // Asegurarse de liberar recursos del log
            base.OnExit(e);
        }
    }
}
