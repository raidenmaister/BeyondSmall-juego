using UnityEngine;
using UnityEngine.SceneManagement;

public class MainMenu : MonoBehaviour
{
    public GameObject optionsMenu;
    // Cambiamos el nombre para que no choque con la clase MainMenu
    public GameObject mainMenuPanel; 

    public void OpenOptionsPanel()
    {
        // Usamos SetActive con 'S' mayúscula
        mainMenuPanel.SetActive(false); 
        optionsMenu.SetActive(true);
    }

    public void OpenMainMenuPanel()
    {
        mainMenuPanel.SetActive(true);
        optionsMenu.SetActive(false);
    }

    public void QuitGame()
    {
        Application.Quit();
        // Tip extra: Application.Quit() no cierra el juego si lo pruebas dentro del editor de Unity, 
        // pero funcionará perfecto cuando exportes (build) tu juego. 😉
    }

    public void PlayGame()
    {
        SceneManager.LoadScene("Class_rom_exemple");
    }
}