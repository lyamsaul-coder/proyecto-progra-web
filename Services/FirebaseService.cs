using FirebaseAdmin;
using Google.Apis.Auth.OAuth2;
using Google.Cloud.Firestore;
using Google.Cloud.Firestore.V1;
using Grpc.Auth;
using Microsoft.AspNetCore.Builder.Extensions;
using Newtonsoft.Json;
using System.Text.Json.Serialization;
namespace Proyecto_Progra_Web.API.Services;

public class FirebaseService
{
    /*
     * _firestoreDb: Instancia de la base de datos FS
     * Se guarda como variable privada porque solo este servicio lo maneja
     */

    private readonly FirestoreDb _firestoreDb;

    /*
     * _logger: Para registrar eventos (errores, informacion)
     * Nos permite ver que esta pasando en la consola / logs
     */
    private readonly ILogger<FirebaseService> _logger;

    /*
     * Constructor: Se ejecuta cuando la app arranca
     * Recibe un ILogger inyectado por ASP.NET Core
     */
    public FirebaseService(ILogger<FirebaseService> logger)
    {
        _logger = logger;

        try
        {
            /*
             * Paso 1: Obtener la ruta del archivo de las cred
             * AppContext.BaseDirectory: Directorio / folder raiz de la app
             * Path.Combine: Une las rutas de forma segura
             */
            var credentialsPath = Path.Combine(
                AppContext.BaseDirectory,
                "Config",
                "firebase-credentials.json"
            );

            /*
             * Paso 2: Validar que el archivo existe
             * Si no existe, lanzamos una excepcion para detener la app
             */

            if (!File.Exists(credentialsPath))
            {
                throw new FileNotFoundException(
                    $"Archivo de credenciales no encontrado en: {credentialsPath}"
                    );
            }

            /*
             * Paso 3: Extraer el project id del archivo JSON
             * El archivo contiene multiples propiedades, pero solo ocupamos el pid
             */
            var projectId = GetProjectIdFromCredentials(credentialsPath);

            /*
             * Paso 4: Establecer variable de entorno GOOGLE_APPLICATION_CRENDENTIALS
             * Esta es una variable de entorno que Google SDK busca automaticamente
             * Cuando esta configurada, las librerias saben donde buscar las credenciales
             * Sin esto, Google no sabe a que proyecto consumir
             */
            Environment.SetEnvironmentVariable(
                "GOOGLE_APPLICATION_CREDENTIALS",
                credentialsPath
                );

            /*
             * Paso 5: Inicializar FB SDK
             * FB necesita credenciales para saber a que proyecto conectarse
             * GoogleCredential.FromFile: lee y parsea el archivo JSON de cred
             * FirebaseApp.Create: Registra la app de FB en memoria
             */
            if (FirebaseApp.DefaultInstance == null)
            {
                FirebaseApp.Create(
                    new AppOptions
                    {
                        Credential = GoogleCredential.FromFile(credentialsPath)
                    }
                );
            }

            /*
             * Paso 6: Crear el cliente Firestore
             * Necesitamos:
             * 1. Crendenciales (G)
             * 2. Convertir esas credenciales a ChannelCredentials (formato que Google Cloud entiende)
             * 3. Pasarle los scopes (permisos) que necesita
             */
            var firebaseClientBuilder = new FirestoreClientBuilder
            {
                ChannelCredentials = GoogleCredential.FromFile(credentialsPath)
                    .CreateScoped("https://www.googleapis.com/auth/cloud-platform")
                    .ToChannelCredentials()

            };

            /*
             * Paso 7: Construir el cliente
             * Build() crea una instancia / levanta la instancia del cliente con las credenciales
             */
            var firestoreClient = firebaseClientBuilder.Build();

            /*
             * Paso 8: Crear la instancia del firestore
             * Cliente listo y configurado / crendenciales listas y configuradas
             * Necesita:
             * projectid: para saber a que proyecto conectarse
             * firestoreClient: el cliente configurado con las cred
             */

            /*
             * Paso 9: Registrar exito
             * Esto aparecera en la consola de rider/vs
             */
            _firestoreDb = FirestoreDb.Create(projectId, firestoreClient);
            Console.WriteLine("Conexión a Firebase iniciada correctamente");
        }
        catch (Exception e)
        {
            /*
             * Si algo falla en cualquier paso:
             * 1. Registramos el error en la consola
             * 2. Registramos el stack trace (donde exactamente fallo)
             * 3. Relanzamos la excepcion para detener la app
             */

            Console.WriteLine($"Error al iniciar Firebase: {e.Message}");
            Console.WriteLine($"Stack trace: {e.StackTrace}");
            throw;
        }
    }

    private string GetProjectIdFromCredentials(string credentialsPath)
    {
        //Primero: Leer el archivo JSON como string
        //File.ReadAllText: lee todo el contenido del archivo en memoria
        var json = File.ReadAllText(credentialsPath);

        //Segundo: Parseamos el JSON (convertimos a un objeto dinamico)
        //JsonConvert.DeserializeObject: convierte el string JSON a un objeto C#
        //dynamic: Tipo flexible que permite acceder a propiedades en tiempo de ejecucion
        dynamic credentials = JsonConvert.DeserializeObject(json);

        //Tercero: Extraer y devolver el project id
        //credentials["project_id"]: acceder a la propiedad id del JSON
        return credentials["project_id"];
    }

    public CollectionReference GetCollection(string collectionName)
    {
        /*
         * Devolvemos una referencia a la coleccion
         * GetCollectionReference: no descarga, apunta a los datos
         * Es como un apuntador a la coleccion
         * 
         */
        return _firestoreDb.Collection(collectionName);

    }
}

